using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Utility.Assets;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using HdtApi = Hearthstone_Deck_Tracker.API.Core;

namespace HistoryCombatSimulation
{
	public sealed class HistoryOverlay
	{
		private readonly Canvas _layer = new Canvas { Visibility = Visibility.Collapsed };
		private readonly SolidColorBrush _background = new SolidColorBrush(Color.FromArgb(224, 18, 20, 23));
		private readonly Border _visual;
		private readonly StackPanel _content = new StackPanel();
		private readonly TextBlock _title = Text("COMBAT HISTORY", 13, FontWeights.Bold);
		private readonly Button _collapse = new Button { Content = "−", Width = 21, Height = 18, Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "Collapse" };
		private readonly TextBlock _summary = Text(string.Empty, 11, FontWeights.Normal);
		private readonly Grid _columns = new Grid { Height = 18 };
		private readonly TextBlock _older = Text(string.Empty, 10, FontWeights.Normal);
		private readonly StackPanel _rows = new StackPanel();
		private readonly ScrollBar _scroll = new ScrollBar { Orientation = Orientation.Vertical, Width = 8, Minimum = 0, SmallChange = 1, LargeChange = 5, Visibility = Visibility.Collapsed };
		private readonly Dictionary<CombatRow, RowVisual> _rowVisuals = new Dictionary<CombatRow, RowVisual>();
		private HistoryLayout? _layout;
		private bool? _showDamageColumn;
		private bool? _showHeroColumn;
		private IReadOnlyList<CombatRow> _lastRows = Array.Empty<CombatRow>();
		private PluginSettings? _lastSettings;
		private bool _attached;
		private Canvas? _canvas;
		private bool _changingScroll;
		private int _lastRowCount;
		private int? _visibleRows;
		private readonly IReadOnlyList<CombatRow> _previewRows = CreatePreviewRows();
		private bool _dragging;
		private Point _dragStart;
		private double _dragLeft;
		private double _dragTop;
		private bool _collapsed;
		private bool _hasAppliedPosition;
		private OverlaySide _appliedSide;
		private double _appliedHorizontalOffset;
		private double _appliedVerticalOffset;
		private double _appliedCanvasWidth = double.NaN;
		public event EventHandler? PositionChanged;

		public HistoryOverlay()
		{
			_visual = new Border { Background = _background, CornerRadius = new CornerRadius(5), Padding = new Thickness(7), IsHitTestVisible = true };
			_title.ToolTip = "Anomaly markers appear when the most likely result did not happen: ! above 50%, !! at least 80%, !!! at least 95% expected probability by default.";
			var header = new DockPanel(); DockPanel.SetDock(_collapse, Dock.Right); header.Children.Add(_collapse); header.Children.Add(_title); _content.Children.Add(header); _content.Children.Add(_summary); _content.Children.Add(_columns); _content.Children.Add(_older); _content.Children.Add(_rows);
			_visual.Child = _content; _layer.Children.Add(_visual); _layer.Children.Add(_scroll);
			_scroll.ValueChanged += (_, __) => { if(!_changingScroll && _lastSettings != null) RenderViewport(_lastRows, _lastSettings); };
			_visual.MouseLeftButtonDown += BeginDrag; _visual.MouseMove += Drag; _visual.MouseLeftButtonUp += EndDrag;
			_collapse.Click += (_, __) => ToggleCollapsed();
		}

		public bool Attach()
		{
			var canvas = HdtApi.OverlayCanvas;
			if(canvas == null)
				return false;
			if(_attached && ReferenceEquals(_canvas, canvas) && canvas.Children.Contains(_layer))
				return false;
			_canvas?.Children.Remove(_layer);
			if(!canvas.Children.Contains(_layer))
				canvas.Children.Add(_layer);
			OverlayExtensions.SetIsOverlayHitTestVisible(_scroll, true);
			OverlayExtensions.SetIsOverlayHitTestVisible(_collapse, true);
			OverlayExtensions.SetIsOverlayHoverVisible(_visual, true);
			_canvas = canvas;
			_attached = true;
			return true;
		}

		public void Detach()
		{
			if(!_attached)
				return;
			OverlayExtensions.SetIsOverlayHitTestVisible(_scroll, false);
			OverlayExtensions.SetIsOverlayHitTestVisible(_collapse, false);
			OverlayExtensions.SetIsOverlayHitTestVisible(_visual, false);
			OverlayExtensions.SetIsOverlayHoverVisible(_visual, false);
			_canvas?.Children.Remove(_layer);
			_changingScroll = true;
			_scroll.Value = 0;
			_changingScroll = false;
			_rows.Children.Clear();
			_rowVisuals.Clear();
			_lastRows = Array.Empty<CombatRow>();
			_lastSettings = null;
			_layer.Visibility = Visibility.Collapsed;
			_canvas = null;
			_attached = false;
			_hasAppliedPosition = false;
		}

		public void Hide() => _layer.Visibility = Visibility.Collapsed;

		public void Update(IReadOnlyList<CombatRow> rows, PluginSettings settings, bool shouldShow)
		{
			if(!_attached || !settings.Enabled || !shouldShow) { Hide(); return; }
			var previousLeft = Canvas.GetLeft(_layer);
			var previousTop = Canvas.GetTop(_layer);
			var positionInputChanged = !_hasAppliedPosition
				|| _appliedSide != settings.Side
				|| Math.Abs(_appliedHorizontalOffset - settings.HorizontalOffset) >= .01
				|| Math.Abs(_appliedVerticalOffset - settings.VerticalOffset) >= .01;
			var canvasWidth = _canvas?.ActualWidth ?? double.NaN;
			var canvasWidthUnchanged = IsFinite(canvasWidth) && Math.Abs(_appliedCanvasWidth - canvasWidth) < .1;
			var preview = rows.Count == 0 && (settings.ShowOverlayPreview || !settings.LockOverlayPosition);
			if(rows.Count == 0 && !preview)
			{
				_lastRowCount = 0;
				_visibleRows = null;
				_changingScroll = true;
				_scroll.Value = 0;
				_changingScroll = false;
				_rows.Children.Clear();
				_rowVisuals.Clear();
				_lastRows = Array.Empty<CombatRow>();
				_lastSettings = null;
				Hide();
				return;
			}
			var displayRows = preview ? _previewRows : rows;
			settings.Normalize(); _lastRows = displayRows; _lastSettings = settings;
			_title.ToolTip = settings.StrictAnomalies ? "Strict anomalies: one unique most likely result did not happen. ! for any miss, !! from 80%, !!! from 95%. Equal highest chances are not marked." : "Anomaly markers appear when the most likely result did not happen: ! above the configured light threshold, !! above the strong threshold, !!! above the extreme threshold.";
			if(preview)
				foreach(var row in displayRows) row.Anomaly = AnomalyClassifier.Classify(row.Outcome, row.Probabilities, settings.GetThresholds(), settings.StrictAnomalies);
			var containsStaleRows = _rowVisuals.Keys.Any(existing => !displayRows.Contains(existing));
			if(_layout != settings.Layout || _showDamageColumn != settings.ShowDamageColumn || _showHeroColumn != settings.ShowHeroColumn || containsStaleRows)
			{
				_layout = settings.Layout; _showDamageColumn = settings.ShowDamageColumn; _showHeroColumn = settings.ShowHeroColumn; _rows.Children.Clear(); _rowVisuals.Clear(); ConfigureColumnHeader(settings.Layout, settings.ShowHeroColumn, settings.ShowDamageColumn);
			}
			foreach(var row in displayRows)
			{
				if(!_rowVisuals.TryGetValue(row, out var visual))
				{
					visual = new RowVisual(settings.Layout, settings.ShowHeroColumn, settings.ShowDamageColumn); _rowVisuals.Add(row, visual); _rows.Children.Add(visual.Grid);
				}
				visual.Update(row, settings);
			}
			_title.Text = preview ? "COMBAT HISTORY   PREVIEW" : "COMBAT HISTORY";
			_summary.Visibility = settings.ShowMatchSummary && settings.Layout == HistoryLayout.Normal ? Visibility.Visible : Visibility.Collapsed;
			if(settings.ShowMatchSummary && settings.Layout == HistoryLayout.Normal)
			{
				var s = AnomalyClassifier.Summarize(displayRows);
				_summary.Text = string.Format(CultureInfo.CurrentCulture, "N {0}   ACTUAL / EXPECTED   W {1}/{2:0.0}   T {3}/{4:0.0}   L {5}/{6:0.0}", s.SampleSize, s.ActualWins, s.ExpectedWins, s.ActualTies, s.ExpectedTies, s.ActualLosses, s.ExpectedLosses);
			}
			var wasAtNewest = Math.Abs(_scroll.Value - _scroll.Maximum) < .5;
			_changingScroll = true; _scroll.Maximum = Math.Max(0, displayRows.Count - settings.VisibleRows); _scroll.ViewportSize = settings.VisibleRows;
			if(HistoryViewportPolicy.ShouldScrollToNewest(_lastRowCount, displayRows.Count, _visibleRows, settings.VisibleRows, wasAtNewest)) _scroll.Value = _scroll.Maximum;
			_scroll.Visibility = displayRows.Count > settings.VisibleRows ? Visibility.Visible : Visibility.Collapsed; _changingScroll = false;
			_lastRowCount = displayRows.Count; _visibleRows = settings.VisibleRows; RenderViewport(displayRows, settings);
			if(!positionInputChanged && canvasWidthUnchanged && IsFinite(previousLeft))
				PreserveTopLeft(settings, previousLeft, previousTop);
			else
				Position(settings);
			RememberAppliedPosition(settings);
			ApplyInteraction(settings); _background.Color = Color.FromArgb((byte)Math.Round(settings.BackgroundOpacity * 255), 18, 20, 23); _layer.Opacity = settings.Opacity; _layer.RenderTransform = new ScaleTransform(settings.Scale, settings.Scale); _layer.Visibility = Visibility.Visible;
		}

		private void PreserveTopLeft(PluginSettings settings, double left, double top)
		{
			var canvas = _canvas;
			if(canvas == null || canvas.ActualWidth <= 0)
				return;
			Canvas.SetLeft(_layer, left);
			Canvas.SetTop(_layer, IsFinite(top) ? top : settings.VerticalOffset);
			// This is a transient geometry correction. Persisting a derived right-side
			// offset here makes small startup-size differences accumulate across sessions.
			// Only an explicit drag or settings change is allowed to modify the saved offset.
		}

		private void RememberAppliedPosition(PluginSettings settings)
		{
			_hasAppliedPosition = true;
			_appliedSide = settings.Side;
			_appliedHorizontalOffset = settings.HorizontalOffset;
			_appliedVerticalOffset = settings.VerticalOffset;
			_appliedCanvasWidth = _canvas?.ActualWidth ?? double.NaN;
		}

		private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

		private void RenderViewport(IReadOnlyList<CombatRow> rows, PluginSettings settings)
		{
			var start = Math.Max(0, Math.Min((int)Math.Round(_scroll.Value), Math.Max(0, rows.Count - settings.VisibleRows)));
			var end = Math.Min(rows.Count, start + settings.VisibleRows);
			for(var i = 0; i < rows.Count; i++)
				if(_rowVisuals.TryGetValue(rows[i], out var visual))
					visual.Grid.Visibility = i >= start && i < end ? Visibility.Visible : Visibility.Collapsed;
			var newer = rows.Count - end;
			_older.Visibility = start > 0 || newer > 0 ? Visibility.Visible : Visibility.Collapsed;
			_older.Text = start > 0 && newer > 0 ? "+" + start + " older  •  +" + newer + " newer" : start > 0 ? "+" + start + " older" : newer > 0 ? "+" + newer + " newer" : string.Empty;
			ApplyCollapsedVisibility(settings);
			_visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			Canvas.SetLeft(_scroll, _visual.DesiredSize.Width + 2); Canvas.SetTop(_scroll, 4); _scroll.Height = Math.Max(20, _visual.DesiredSize.Height - 8);
			_layer.Width = _visual.DesiredSize.Width + (_scroll.Visibility == Visibility.Visible ? 10 : 0); _layer.Height = _visual.DesiredSize.Height;
		}

		private void Position(PluginSettings settings)
		{
			var canvas = _canvas;
			if(canvas == null)
				return;
			var width = canvas.ActualWidth;
			if(width <= 0)
				return;
			_visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			var scaledWidth = (_visual.DesiredSize.Width + (_scroll.Visibility == Visibility.Visible ? 10 : 0)) * settings.Scale;
			var left = settings.Side == OverlaySide.Left ? settings.HorizontalOffset : width - scaledWidth - settings.HorizontalOffset;
			Canvas.SetLeft(_layer, left); Canvas.SetTop(_layer, settings.VerticalOffset);
		}

		private void ApplyInteraction(PluginSettings settings)
		{
			var unlocked = !settings.LockOverlayPosition; _visual.IsHitTestVisible = true; _visual.Cursor = unlocked ? Cursors.SizeAll : Cursors.Arrow; OverlayExtensions.SetIsOverlayHitTestVisible(_visual, unlocked);
		}

		private void BeginDrag(object sender, MouseButtonEventArgs e)
		{
			var canvas = _canvas;
			if(canvas == null || _lastSettings == null || _lastSettings.LockOverlayPosition || _collapse.IsMouseOver)
				return;
			_dragging = true;
			_dragStart = e.GetPosition(canvas);
			_dragLeft = Canvas.GetLeft(_layer);
			_dragTop = Canvas.GetTop(_layer);
			_visual.CaptureMouse();
			e.Handled = true;
		}

		private void ToggleCollapsed()
		{
			if(!_collapsed) { _visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); _visual.Width = _visual.DesiredSize.Width; _collapsed = true; }
			else { _collapsed = false; _visual.Width = double.NaN; }
			_collapse.Content = _collapsed ? "+" : "−"; _collapse.ToolTip = _collapsed ? "Expand" : "Collapse";
			if(_lastSettings != null && _lastRows.Count > 0) { RenderViewport(_lastRows, _lastSettings); Position(_lastSettings); }
		}

		private void ApplyCollapsedVisibility(PluginSettings settings)
		{
			_rows.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible; _columns.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
			_summary.Visibility = !_collapsed && settings.ShowMatchSummary && settings.Layout == HistoryLayout.Normal ? Visibility.Visible : Visibility.Collapsed;
			if(_collapsed) _older.Visibility = Visibility.Collapsed;
			_scroll.Visibility = !_collapsed && _lastRows.Count > settings.VisibleRows ? Visibility.Visible : Visibility.Collapsed;
		}

		private void Drag(object sender, MouseEventArgs e)
		{
			var canvas = _canvas;
			if(!_dragging || _lastSettings == null || canvas == null || e.LeftButton != MouseButtonState.Pressed)
				return;
			var current = e.GetPosition(canvas); var left = _dragLeft + current.X - _dragStart.X; var top = _dragTop + current.Y - _dragStart.Y;
			Canvas.SetLeft(_layer, left); Canvas.SetTop(_layer, top); _lastSettings.VerticalOffset = top;
			var scaledWidth = _layer.Width * _lastSettings.Scale; _lastSettings.HorizontalOffset = OverlayPositionPolicy.HorizontalOffsetFromLeft(_lastSettings.Side, canvas.ActualWidth, scaledWidth, left);
			e.Handled = true;
		}

		private void EndDrag(object sender, MouseButtonEventArgs e)
		{
			if(!_dragging) return; _dragging = false; _visual.ReleaseMouseCapture(); _lastSettings?.Normalize(); PositionChanged?.Invoke(this, EventArgs.Empty); e.Handled = true;
		}

		private void ConfigureColumnHeader(HistoryLayout layout, bool showHero, bool showDamage)
		{
			_columns.Children.Clear(); _columns.ColumnDefinitions.Clear();
			var column = 0; AddHeaderColumn("#", layout == HistoryLayout.Normal ? 24 : 22, column++);
			if(showHero) AddHeaderColumn("HERO", layout == HistoryLayout.Normal ? 38 : 30, column++);
			if(layout == HistoryLayout.Normal)
			{
				AddHeaderColumn("WIN", 42, column++); AddHeaderColumn("TIE", 42, column++); AddHeaderColumn("LOSS", 42, column++);
			}
			else AddHeaderColumn("W/T/L", 76, column++);
			if(showDamage) AddHeaderColumn("DMG", 40, column++);
			AddHeaderColumn("RESULT", layout == HistoryLayout.Normal ? 62 : 54, column);
		}

		private void AddHeaderColumn(string value, double width, int column)
		{
			_columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) }); AddHeader(value, column);
		}

		private void AddHeader(string value, int column)
		{
			var label = Text(value, 9, FontWeights.Bold); label.TextAlignment = TextAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center; System.Windows.Controls.Grid.SetColumn(label, column); _columns.Children.Add(label);
		}

		private static TextBlock Text(string text, double size, FontWeight weight) => new TextBlock { Text = text, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = size, FontWeight = weight };

		private static IReadOnlyList<CombatRow> CreatePreviewRows()
		{
			return new[]
			{
				PreviewRow(5, "TB_BaconShop_HERO_01", new SimulationProbabilities(.74, .04, .22), CombatOutcome.Tie, 0),
				PreviewRow(6, "TB_BaconShop_HERO_08", new SimulationProbabilities(.62, .03, .35), CombatOutcome.Win, 12),
				PreviewRow(7, "TB_BaconShop_HERO_16", new SimulationProbabilities(.96, .01, .03), CombatOutcome.Loss, -9, true)
			};
		}

		private static CombatRow PreviewRow(int turn, string hero, SimulationProbabilities probabilities, CombatOutcome outcome, int damage, bool ghost = false)
		{
			return new CombatRow(new CombatSnapshot(turn, turn, turn, hero, ghost, 40, 40)) { Probabilities = probabilities, Outcome = outcome, CombatDamage = damage, IsFinalized = true };
		}

		private sealed class RowVisual
		{
			public RowVisual(HistoryLayout layout, bool showHero, bool showDamage)
			{
				Layout = layout; ShowHero = showHero; Grid = new Grid { Height = layout == HistoryLayout.Compact ? 24 : 31, Margin = new Thickness(0, 1, 0, 0) };
				var column = 0; AddColumn(layout == HistoryLayout.Normal ? 24 : 22); Turn = AddText(column++);
				PortraitImage = new Image { Stretch = Stretch.Uniform }; Portrait = new Border { Width = layout == HistoryLayout.Compact ? 23 : 29, Height = layout == HistoryLayout.Compact ? 23 : 29, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), ClipToBounds = true, Child = PortraitImage };
				GhostMarker = Text("\u2620", layout == HistoryLayout.Compact ? 9 : 10, FontWeights.Bold); GhostMarker.Foreground = Brushes.White; GhostMarker.Background = Brushes.Black; GhostMarker.HorizontalAlignment = HorizontalAlignment.Right; GhostMarker.VerticalAlignment = VerticalAlignment.Bottom; GhostMarker.Visibility = Visibility.Collapsed;
				if(showHero)
				{
					AddColumn(layout == HistoryLayout.Normal ? 38 : 30); var portraitCell = new Grid(); portraitCell.Children.Add(Portrait); portraitCell.Children.Add(GhostMarker); System.Windows.Controls.Grid.SetColumn(portraitCell, column++); Grid.Children.Add(portraitCell);
				}
				if(layout == HistoryLayout.Normal)
				{
					AddColumn(42); Win = AddText(column++); AddColumn(42); Tie = AddText(column++); AddColumn(42); Loss = AddText(column++);
				}
				else { AddColumn(76); Compact = AddText(column++); }
				if(showDamage) { AddColumn(40); Damage = AddText(column++); }
				AddColumn(layout == HistoryLayout.Normal ? 62 : 54); var resultColumn = column;
				var result = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
				Outcome = Text(string.Empty, layout == HistoryLayout.Compact ? 11 : 12, FontWeights.SemiBold); Outcome.VerticalAlignment = VerticalAlignment.Center; result.Children.Add(Outcome);
				AnomalyText = Text(string.Empty, layout == HistoryLayout.Compact ? 9 : 10, FontWeights.Bold); AnomalyBadge = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 1), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(1), Child = AnomalyText, Visibility = Visibility.Collapsed }; result.Children.Add(AnomalyBadge);
				System.Windows.Controls.Grid.SetColumn(result, resultColumn); Grid.Children.Add(result);
			}

			public HistoryLayout Layout { get; }
			private bool ShowHero { get; }
			public Grid Grid { get; }
			private TextBlock Turn { get; }
			private Border Portrait { get; }
			private TextBlock? Win { get; }
			private TextBlock? Tie { get; }
			private TextBlock? Loss { get; }
			private TextBlock? Compact { get; }
			private TextBlock? Damage { get; }
			private TextBlock Outcome { get; }
			private Border AnomalyBadge { get; }
			private TextBlock AnomalyText { get; }
			private Image PortraitImage { get; }
			private TextBlock GhostMarker { get; }
			private string? _loadedHeroCardId;
			private Card? _loadedHeroCard;
			private bool _usingPreferredPortrait;
			private bool _portraitDownloadInFlight;
			private int _portraitRequestId;

			public void Update(CombatRow row, PluginSettings settings)
			{
				var culture = CultureInfo.CurrentCulture; Turn.Text = row.Snapshot.Turn.ToString(culture); var p = row.Probabilities;
				if(Layout == HistoryLayout.Normal) { Win!.Text = HistoryFormatting.NormalProbability(p?.Win, culture); Tie!.Text = HistoryFormatting.NormalProbability(p?.Tie, culture); Loss!.Text = HistoryFormatting.NormalProbability(p?.Loss, culture); }
				else Compact!.Text = HistoryFormatting.CompactProbabilities(p, culture);
				if(Damage != null) { var damage = row.CombatDamage.GetValueOrDefault(); Damage.Text = HistoryFormatting.CombatDamage(row.CombatDamage, culture); Damage.Foreground = !row.CombatDamage.HasValue ? Brushes.LightGray : damage > 0 ? Brushes.LightGreen : damage < 0 ? Brushes.Salmon : Brushes.Khaki; }
				Outcome.Text = HistoryFormatting.OutcomeLetter(row.Outcome); Outcome.Foreground = row.Outcome == CombatOutcome.Win ? Brushes.LightGreen : row.Outcome == CombatOutcome.Loss ? Brushes.Salmon : row.Outcome == CombatOutcome.Tie ? Brushes.Khaki : Brushes.LightGray;
				var marker = settings.ShowAnomalyStatus && row.Probabilities != null && row.Outcome != CombatOutcome.Unknown ? HistoryFormatting.AnomalyMarker(row.Anomaly) : string.Empty;
				AnomalyText.Text = marker; AnomalyBadge.Visibility = marker.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
				if(marker.Length > 0)
				{
					var extreme = row.Anomaly == AnomalySeverity.Extreme; AnomalyText.Foreground = extreme ? Brushes.White : Brushes.Black;
					AnomalyBadge.Background = extreme ? Brushes.OrangeRed : Brushes.Gold; AnomalyBadge.BorderBrush = extreme ? Brushes.LightSalmon : Brushes.Khaki;
				}
				if(ShowHero)
				{
					if(!string.Equals(_loadedHeroCardId, row.Snapshot.HeroCardId, StringComparison.Ordinal))
					{
						_loadedHeroCardId = row.Snapshot.HeroCardId; _loadedHeroCard = Database.GetCardFromId(_loadedHeroCardId); _portraitRequestId++; _portraitDownloadInFlight = false; SetBestCachedPortrait(_loadedHeroCard);
					}
					else if(!_usingPreferredPortrait && _loadedHeroCard != null)
					{
						var preferred = AssetDownloaders.cardPortraitDownloader?.TryGetAssetData(_loadedHeroCard, false);
						if(preferred != null) { PortraitImage.Source = preferred; PortraitImage.Stretch = Stretch.Uniform; Portrait.Background = Brushes.Black; _usingPreferredPortrait = true; }
						else RequestMissingPortrait(_loadedHeroCard);
					}
					GhostMarker.Visibility = row.Snapshot.IsGhost ? Visibility.Visible : Visibility.Collapsed;
				}
			}

			private void SetBestCachedPortrait(Card? card)
			{
				if(card == null) { PortraitImage.Source = null; Portrait.Background = Brushes.DimGray; _usingPreferredPortrait = false; return; }
				try
				{
					var preferred = AssetDownloaders.cardPortraitDownloader?.TryGetAssetData(card, false);
					var portrait = preferred ?? AssetDownloaders.cardTileDownloader?.TryGetAssetData(card, false);
					PortraitImage.Source = portrait; PortraitImage.Stretch = preferred != null ? Stretch.Uniform : Stretch.UniformToFill; Portrait.Background = portrait != null ? Brushes.Black : card.Background; _usingPreferredPortrait = preferred != null;
					if(preferred == null) RequestMissingPortrait(card);
				}
				catch { PortraitImage.Source = null; Portrait.Background = card.Background; _usingPreferredPortrait = false; }
			}

			private async void RequestMissingPortrait(Card card)
			{
				var downloader = AssetDownloaders.cardPortraitDownloader; if(downloader == null || _portraitDownloadInFlight) return;
				var requestId = _portraitRequestId; _portraitDownloadInFlight = true;
				try
				{
					var portrait = await downloader.GetAssetData(card);
					if(requestId != _portraitRequestId || !ReferenceEquals(card, _loadedHeroCard) || portrait == null) return;
					PortraitImage.Source = portrait; PortraitImage.Stretch = Stretch.Uniform; Portrait.Background = Brushes.Black; _usingPreferredPortrait = true;
				}
				catch { }
				finally { if(requestId == _portraitRequestId) _portraitDownloadInFlight = false; }
			}

			private void AddColumn(double width) => Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
			private TextBlock AddText(int column) { var text = Text(string.Empty, Layout == HistoryLayout.Compact ? 11 : 12, FontWeights.SemiBold); text.TextAlignment = TextAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center; System.Windows.Controls.Grid.SetColumn(text, column); Grid.Children.Add(text); return text; }
		}
	}
}
