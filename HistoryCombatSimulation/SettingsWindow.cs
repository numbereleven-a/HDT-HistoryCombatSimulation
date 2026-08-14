using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HistoryCombatSimulation
{
	public sealed class SettingsWindow : Window
	{
		private readonly PluginSettings _settings;
		private readonly Action<PluginSettings> _applied;
		private readonly ComboBox _layout = new ComboBox { ItemsSource = Enum.GetValues(typeof(HistoryLayout)), Margin = new Thickness(4) };
		private readonly ComboBox _side = new ComboBox { ItemsSource = Enum.GetValues(typeof(OverlaySide)), Margin = new Thickness(4) };
		private readonly ComboBox _backgroundMode = new ComboBox { ItemsSource = Enum.GetValues(typeof(OverlayBackgroundMode)), Margin = new Thickness(4) };
		private readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();
		private readonly Dictionary<string, TextBlock> _values = new Dictionary<string, TextBlock>();
		private readonly Dictionary<string, string> _formats = new Dictionary<string, string>();
		private readonly Dictionary<string, FrameworkElement> _sliderRows = new Dictionary<string, FrameworkElement>();
		private readonly VersionChecker _versionChecker = new VersionChecker();
		private readonly Version _installedVersion;
		private readonly CancellationTokenSource _closeCancellation = new CancellationTokenSource();
		private readonly TextBlock _versionText = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis };
		private bool _closed;
		private readonly CheckBox _enabled = Check("Enabled");
		private readonly CheckBox _summary = Check("Expected vs actual summary");
		private readonly CheckBox _lockPosition = Check("Lock overlay position");
		private readonly CheckBox _hideWhenUnfocused = Check("Hide overlay when Hearthstone is not in focus");
		private readonly CheckBox _showAnomalies = Check("Show anomaly markers (! / !! / !!!)");
		private readonly CheckBox _showDamage = Check("Show combat damage column");
		private readonly CheckBox _showHero = Check("Show hero column");
		private readonly CheckBox _showPreview = Check("Show overlay preview");
		private readonly CheckBox _strictAnomalies = Check("Strict anomaly mode");

		public SettingsWindow(PluginSettings settings, Action<PluginSettings> applied, Version installedVersion)
		{
			_settings = settings; _applied = applied; _installedVersion = installedVersion; Title = "History Combat Simulation settings"; Width = 500; Height = 720; MinWidth = 500; MinHeight = 600; ResizeMode = ResizeMode.CanResize;
			_showPreview.ToolTip = "Show example combat rows when no combat history is available.";
			ConfigureOwner();
			var generalPanel = new StackPanel { Margin = new Thickness(14) };
			generalPanel.Children.Add(_enabled);
			generalPanel.Children.Add(_strictAnomalies);
			AddSlider(generalPanel, "! expected result, %", "unusual", 51, 99, 1, "0");
			AddSlider(generalPanel, "!! expected result, %", "very", 51, 99, 1, "0");
			AddSlider(generalPanel, "!!! expected result, %", "extreme", 50, 100, 1, "0");
			generalPanel.Children.Add(_showAnomalies);
			generalPanel.Children.Add(new TextBlock { Text = "Table columns", Margin = new Thickness(4, 18, 4, 4), FontWeight = FontWeights.SemiBold });
			generalPanel.Children.Add(new TextBlock { Text = "Compact order: Win / Tie / Loss", Margin = new Thickness(4, 2, 4, 4), Foreground = Brushes.DimGray });
			generalPanel.Children.Add(_summary); generalPanel.Children.Add(_showDamage); generalPanel.Children.Add(_showHero);
			generalPanel.Children.Add(new TextBlock { Text = "Normal mode uses the three percentage thresholds and requires one result above 50%. Strict mode ignores those sliders: if one result has the unique highest chance but another result happens, it shows !; fixed 80% and 95% expected chances still produce !! and !!!. Equal highest chances are not an anomaly.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(4, 18, 4, 2) });

			var visualPanel = new StackPanel { Margin = new Thickness(14) };
			visualPanel.Children.Add(Label("Layout", _layout)); visualPanel.Children.Add(Label("Placement", _side));
			AddSlider(visualPanel, "Horizontal offset", "x", PluginSettings.MinimumPositionOffset, PluginSettings.MaximumPositionOffset, 1, "0");
			AddSlider(visualPanel, "Vertical offset", "y", PluginSettings.MinimumPositionOffset, PluginSettings.MaximumPositionOffset, 1, "0");
			AddSlider(visualPanel, "Scale", "scale", .5, 2, .05, "0.00");
			AddSlider(visualPanel, "Overlay opacity", "opacity", .2, 1, .05, "0.00");
			visualPanel.Children.Add(Label("Background mode", _backgroundMode));
			AddSlider(visualPanel, "Background opacity", "backgroundOpacity", 0, 1, .05, "0.00");
			AddSlider(visualPanel, "Visible rows", "rows", 6, 20, 1, "0");
			visualPanel.Children.Add(_hideWhenUnfocused); visualPanel.Children.Add(_showPreview); visualPanel.Children.Add(_lockPosition);
			var resetRow = new DockPanel { Margin = new Thickness(4, 10, 4, 2) };
			var reset = new Button { Content = "Reset visual settings", Width = 145, HorizontalAlignment = HorizontalAlignment.Left }; reset.Click += (_, __) => LoadVisualValues(new PluginSettings()); resetRow.Children.Add(reset); visualPanel.Children.Add(resetRow);

			var footer = new Grid { Margin = new Thickness(4, 10, 4, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			var versionRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			var checkUpdates = new Button { Content = "↻", Width = 22, Height = 20, Padding = new Thickness(0), ToolTip = "Check for updates", Margin = new Thickness(0, 0, 5, 0) };
			checkUpdates.Click += async (_, __) => await CheckForUpdatesAsync(checkUpdates);
			versionRow.Children.Add(checkUpdates);
			_versionText.Text = "Version " + _installedVersion;
			versionRow.Children.Add(_versionText); footer.Children.Add(versionRow);
			var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(buttons, 1);
			var apply = new Button { Content = "Apply", Width = 72, Margin = new Thickness(4) }; apply.Click += (_, __) => Apply();
			var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(4), IsDefault = true }; ok.Click += (_, __) => { Apply(); Close(); };
			var cancel = new Button { Content = "Cancel", Width = 72, Margin = new Thickness(4) }; cancel.Click += (_, __) => Close();
			buttons.Children.Add(apply); buttons.Children.Add(ok); buttons.Children.Add(cancel); footer.Children.Add(buttons);
			var bottom = new StackPanel(); footer.Margin = new Thickness(6, 4, 18, 12); bottom.Children.Add(footer);
			var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			var tabs = new TabControl { Margin = new Thickness(8, 8, 8, 0) };
			tabs.Items.Add(new TabItem { Header = "Table", Content = new ScrollViewer { Content = generalPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
			tabs.Items.Add(new TabItem { Header = "Visual", Content = new ScrollViewer { Content = visualPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
			root.Children.Add(tabs); Grid.SetRow(bottom, 1); root.Children.Add(bottom);
			Content = root; _strictAnomalies.Checked += (_, __) => UpdateAnomalySliderState(); _strictAnomalies.Unchecked += (_, __) => UpdateAnomalySliderState(); _backgroundMode.SelectionChanged += (_, __) => UpdateBackgroundOpacityState(); LoadValues(_settings.Snapshot());
			Closed += (_, __) => { _closed = true; _closeCancellation.Cancel(); _closeCancellation.Dispose(); };
		}

		private void ConfigureOwner()
		{
			var owner = Application.Current?.MainWindow;
			if(owner == null || ReferenceEquals(owner, this) || new WindowInteropHelper(owner).Handle == IntPtr.Zero)
			{
				WindowStartupLocation = WindowStartupLocation.CenterScreen;
				return;
			}

			try
			{
				Owner = owner;
				WindowStartupLocation = WindowStartupLocation.CenterOwner;
			}
			catch(InvalidOperationException)
			{
				WindowStartupLocation = WindowStartupLocation.CenterScreen;
			}
		}

		private async System.Threading.Tasks.Task CheckForUpdatesAsync(Button button)
		{
			button.IsEnabled = false;
			SetUpdateStatus("Checking…", null);
			using(var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_closeCancellation.Token))
			{
				try
				{
					var result = await _versionChecker.CheckAsync(_installedVersion, requestCancellation.Token);
					if(_closed) return;
					if(result.UpdateAvailable)
						SetUpdateStatus("update " + result.Latest + " available", result.LatestReleaseUrl);
					else
						SetUpdateStatus("latest version installed", null);
				}
				catch(OperationCanceledException) when(_closed) { }
				catch(Exception ex)
				{
					if(_closed) return;
					SetUpdateStatus("check failed", null);
					PluginLog.Warn("update check failed", ex);
				}
				finally
				{
					if(!_closed) button.IsEnabled = true;
				}
			}
		}

		private void SetUpdateStatus(string text, string? link)
		{
			_versionText.Inlines.Clear();
			_versionText.Inlines.Add(new Run("Version " + _installedVersion + " — "));
			if(link == null) { _versionText.Inlines.Add(new Run(text)); return; }
			var hyperlink = new Hyperlink(new Run(text)) { NavigateUri = new Uri(link, UriKind.Absolute) };
			hyperlink.RequestNavigate += (_, e) =>
			{
				try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
				catch(Exception ex) { PluginLog.Warn("release page could not be opened", ex); }
			};
			hyperlink.ToolTip = "Open the latest release download page";
			_versionText.Inlines.Add(hyperlink);
		}

		private void LoadValues(PluginSettings source)
		{
			_enabled.IsChecked = source.Enabled; _layout.SelectedItem = source.Layout; _side.SelectedItem = source.Side; _backgroundMode.SelectedItem = source.BackgroundMode; _summary.IsChecked = source.ShowMatchSummary; _showAnomalies.IsChecked = source.ShowAnomalyStatus; _showDamage.IsChecked = source.ShowDamageColumn; _showHero.IsChecked = source.ShowHeroColumn; _showPreview.IsChecked = source.ShowOverlayPreview; _strictAnomalies.IsChecked = source.StrictAnomalies; _lockPosition.IsChecked = source.LockOverlayPosition; _hideWhenUnfocused.IsChecked = source.HideWhenHearthstoneNotForeground;
			Set("x", source.HorizontalOffset); Set("y", source.VerticalOffset); Set("scale", source.Scale); Set("opacity", source.Opacity); Set("backgroundOpacity", source.BackgroundOpacity); Set("rows", source.VisibleRows);
			Set("unusual", source.UnusualExpectedPercent); Set("very", source.VeryUnusualExpectedPercent); Set("extreme", source.ExtremeExpectedPercent);
			UpdateAnomalySliderState(); UpdateBackgroundOpacityState();
		}

		private void LoadVisualValues(PluginSettings source)
		{
			_layout.SelectedItem = source.Layout; _side.SelectedItem = source.Side;
			Set("x", source.HorizontalOffset); Set("y", source.VerticalOffset); Set("scale", source.Scale); Set("opacity", source.Opacity); Set("backgroundOpacity", source.BackgroundOpacity); Set("rows", source.VisibleRows);
		}

		private void Apply()
		{
			var candidate = _settings.Snapshot();
			candidate.Enabled = _enabled.IsChecked == true; if(_layout.SelectedItem is HistoryLayout layout) candidate.Layout = layout; if(_side.SelectedItem is OverlaySide side) candidate.Side = side; if(_backgroundMode.SelectedItem is OverlayBackgroundMode backgroundMode) candidate.BackgroundMode = backgroundMode;
			candidate.HorizontalOffset = Get("x"); candidate.VerticalOffset = Get("y"); candidate.Scale = Get("scale"); candidate.Opacity = Get("opacity"); candidate.BackgroundOpacity = Get("backgroundOpacity"); candidate.VisibleRows = (int)Math.Round(Get("rows"));
			candidate.UnusualExpectedPercent = Get("unusual"); candidate.VeryUnusualExpectedPercent = Get("very"); candidate.ExtremeExpectedPercent = Get("extreme"); candidate.ShowMatchSummary = _summary.IsChecked == true; candidate.ShowAnomalyStatus = _showAnomalies.IsChecked == true; candidate.ShowDamageColumn = _showDamage.IsChecked == true; candidate.ShowHeroColumn = _showHero.IsChecked == true; candidate.ShowOverlayPreview = _showPreview.IsChecked == true; candidate.StrictAnomalies = _strictAnomalies.IsChecked == true; candidate.LockOverlayPosition = _lockPosition.IsChecked == true; candidate.HideWhenHearthstoneNotForeground = _hideWhenUnfocused.IsChecked == true;
			candidate.Normalize(); _applied(candidate); LoadValues(_settings.Snapshot());
		}

		public void SyncPosition()
		{
			var snapshot = _settings.Snapshot(); Set("x", snapshot.HorizontalOffset); Set("y", snapshot.VerticalOffset);
		}

		private void AddSlider(Panel panel, string label, string key, double minimum, double maximum, double tick, string format)
		{
			var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
			var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }; grid.Children.Add(caption);
			var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = tick, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) }; Grid.SetColumn(slider, 1); grid.Children.Add(slider);
			var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right }; Grid.SetColumn(value, 2); grid.Children.Add(value);
			slider.ValueChanged += (_, __) => value.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture); _sliders.Add(key, slider); _values.Add(key, value); _sliderRows.Add(key, grid); panel.Children.Add(grid);
			_formats.Add(key, format);
		}

		private void UpdateAnomalySliderState()
		{
			var enabled = _strictAnomalies.IsChecked != true;
			foreach(var key in new[] { "unusual", "very", "extreme" }) if(_sliderRows.TryGetValue(key, out var row)) row.IsEnabled = enabled;
		}

		private void UpdateBackgroundOpacityState()
		{
			if(_sliderRows.TryGetValue("backgroundOpacity", out var row))
				row.IsEnabled = !Equals(_backgroundMode.SelectedItem, OverlayBackgroundMode.Transparent);
		}

		private static FrameworkElement Label(string label, FrameworkElement input) { var panel = new DockPanel(); panel.Children.Add(new TextBlock { Text = label, Width = 165, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }); panel.Children.Add(input); return panel; }
		private static CheckBox Check(string label) => new CheckBox { Content = label, Margin = new Thickness(4) };
		private void Set(string key, double value) { _sliders[key].Value = value; _values[key].Text = _sliders[key].Value.ToString(_formats[key], CultureInfo.CurrentCulture); }
		private double Get(string key) => _sliders[key].Value;
	}
}
