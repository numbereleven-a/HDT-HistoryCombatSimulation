using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HistoryCombatSimulation
{
	public sealed class SettingsWindow : Window
	{
		private readonly PluginSettings _settings;
		private readonly Action _applied;
		private readonly ComboBox _layout = new ComboBox { ItemsSource = Enum.GetValues(typeof(HistoryLayout)), Margin = new Thickness(4) };
		private readonly ComboBox _side = new ComboBox { ItemsSource = Enum.GetValues(typeof(OverlaySide)), Margin = new Thickness(4) };
		private readonly Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();
		private readonly Dictionary<string, TextBlock> _values = new Dictionary<string, TextBlock>();
		private readonly Dictionary<string, FrameworkElement> _sliderRows = new Dictionary<string, FrameworkElement>();
		private readonly CheckBox _enabled = Check("Enabled");
		private readonly CheckBox _summary = Check("Expected vs actual summary");
		private readonly CheckBox _lockPosition = Check("Lock overlay position");
		private readonly CheckBox _hideWhenUnfocused = Check("Hide overlay when Hearthstone is not in focus");
		private readonly CheckBox _showAnomalies = Check("Show anomaly markers (! / !! / !!!)");
		private readonly CheckBox _showDamage = Check("Show combat damage column");
		private readonly CheckBox _showHero = Check("Show hero column");
		private readonly CheckBox _strictAnomalies = Check("Strict anomaly mode");

		public SettingsWindow(PluginSettings settings, Action applied)
		{
			_settings = settings; _applied = applied; Title = "History Combat Simulation settings"; Width = 500; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen; ResizeMode = ResizeMode.NoResize;
			var panel = new StackPanel { Margin = new Thickness(14) };
			panel.Children.Add(_enabled); panel.Children.Add(Label("Layout", _layout)); panel.Children.Add(Label("Placement", _side));
			AddSlider(panel, "Horizontal offset", "x", -500, 500, 1, "0");
			AddSlider(panel, "Vertical offset", "y", -500, 1200, 1, "0");
			AddSlider(panel, "Scale", "scale", .5, 2, .05, "0.00");
			AddSlider(panel, "Overlay opacity", "opacity", .2, 1, .05, "0.00");
			AddSlider(panel, "Background opacity", "backgroundOpacity", 0, 1, .05, "0.00");
			AddSlider(panel, "Visible rows", "rows", 6, 20, 1, "0");
			panel.Children.Add(new TextBlock { Text = "Compact order: Win / Tie / Loss", Margin = new Thickness(4, 10, 4, 4), FontWeight = FontWeights.SemiBold });
			panel.Children.Add(_strictAnomalies);
			AddSlider(panel, "! expected result, %", "unusual", 50, 99, 1, "0");
			AddSlider(panel, "!! expected result, %", "very", 50, 99, 1, "0");
			AddSlider(panel, "!!! expected result, %", "extreme", 50, 100, 1, "0");
			panel.Children.Add(_summary); panel.Children.Add(_showAnomalies); panel.Children.Add(_showDamage); panel.Children.Add(_showHero);
			panel.Children.Add(_hideWhenUnfocused); panel.Children.Add(_lockPosition);

			var resetRow = new DockPanel { Margin = new Thickness(4, 10, 4, 2) };
			var reset = new Button { Content = "Reset visual settings", Width = 145, HorizontalAlignment = HorizontalAlignment.Left }; reset.Click += (_, __) => LoadVisualValues(new PluginSettings()); resetRow.Children.Add(reset); panel.Children.Add(resetRow);

			var footer = new Grid { Margin = new Thickness(4, 10, 4, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			var version = new TextBlock { Text = "Version " + PluginVersion.Display, VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 6) }; footer.Children.Add(version);
			var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(buttons, 1);
			var apply = new Button { Content = "Apply", Width = 80, Margin = new Thickness(4) }; apply.Click += (_, __) => Apply();
			var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(4), IsDefault = true }; ok.Click += (_, __) => { Apply(); Close(); };
			var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4) }; cancel.Click += (_, __) => Close();
			buttons.Children.Add(apply); buttons.Children.Add(ok); buttons.Children.Add(cancel); footer.Children.Add(buttons);
			var anomalyHelp = new TextBlock { Text = "Normal mode uses the three percentage thresholds and requires one result above 50%. Strict mode ignores those sliders: if one result has the unique highest chance but another result happens, it shows !; fixed 80% and 95% expected chances still produce !! and !!!. Equal highest chances are not an anomaly.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(18, 8, 18, 2) };
			var bottom = new StackPanel(); bottom.Children.Add(anomalyHelp); footer.Margin = new Thickness(18, 4, 18, 12); bottom.Children.Add(footer);
			var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll); Grid.SetRow(bottom, 1); root.Children.Add(bottom);
			Content = root; _strictAnomalies.Checked += (_, __) => UpdateAnomalySliderState(); _strictAnomalies.Unchecked += (_, __) => UpdateAnomalySliderState(); LoadValues(_settings);
		}

		private void LoadValues(PluginSettings source)
		{
			_enabled.IsChecked = source.Enabled; _layout.SelectedItem = source.Layout; _side.SelectedItem = source.Side; _summary.IsChecked = source.ShowMatchSummary; _showAnomalies.IsChecked = source.ShowAnomalyStatus; _showDamage.IsChecked = source.ShowDamageColumn; _showHero.IsChecked = source.ShowHeroColumn; _strictAnomalies.IsChecked = source.StrictAnomalies; _lockPosition.IsChecked = source.LockOverlayPosition; _hideWhenUnfocused.IsChecked = source.HideWhenHearthstoneNotForeground;
			Set("x", source.HorizontalOffset); Set("y", source.VerticalOffset); Set("scale", source.Scale); Set("opacity", source.Opacity); Set("backgroundOpacity", source.BackgroundOpacity); Set("rows", source.VisibleRows);
			Set("unusual", source.UnusualExpectedPercent); Set("very", source.VeryUnusualExpectedPercent); Set("extreme", source.ExtremeExpectedPercent);
			UpdateAnomalySliderState();
		}

		private void LoadVisualValues(PluginSettings source)
		{
			_layout.SelectedItem = source.Layout; _side.SelectedItem = source.Side;
			Set("x", source.HorizontalOffset); Set("y", source.VerticalOffset); Set("scale", source.Scale); Set("opacity", source.Opacity); Set("backgroundOpacity", source.BackgroundOpacity); Set("rows", source.VisibleRows);
		}

		private void Apply()
		{
			_settings.Enabled = _enabled.IsChecked == true; if(_layout.SelectedItem is HistoryLayout layout) _settings.Layout = layout; if(_side.SelectedItem is OverlaySide side) _settings.Side = side;
			_settings.HorizontalOffset = Get("x"); _settings.VerticalOffset = Get("y"); _settings.Scale = Get("scale"); _settings.Opacity = Get("opacity"); _settings.BackgroundOpacity = Get("backgroundOpacity"); _settings.VisibleRows = (int)Math.Round(Get("rows"));
			_settings.UnusualExpectedPercent = Get("unusual"); _settings.VeryUnusualExpectedPercent = Get("very"); _settings.ExtremeExpectedPercent = Get("extreme"); _settings.ShowMatchSummary = _summary.IsChecked == true; _settings.ShowAnomalyStatus = _showAnomalies.IsChecked == true; _settings.ShowDamageColumn = _showDamage.IsChecked == true; _settings.ShowHeroColumn = _showHero.IsChecked == true; _settings.StrictAnomalies = _strictAnomalies.IsChecked == true; _settings.LockOverlayPosition = _lockPosition.IsChecked == true; _settings.HideWhenHearthstoneNotForeground = _hideWhenUnfocused.IsChecked == true;
			_settings.Normalize(); _settings.Save(); LoadValues(_settings); _applied();
		}

		public void SyncPosition()
		{
			Set("x", _settings.HorizontalOffset); Set("y", _settings.VerticalOffset);
		}

		private void AddSlider(Panel panel, string label, string key, double minimum, double maximum, double tick, string format)
		{
			var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
			var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }; grid.Children.Add(caption);
			var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = tick, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) }; Grid.SetColumn(slider, 1); grid.Children.Add(slider);
			var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right }; Grid.SetColumn(value, 2); grid.Children.Add(value);
			slider.ValueChanged += (_, __) => value.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture); _sliders.Add(key, slider); _values.Add(key, value); _sliderRows.Add(key, grid); panel.Children.Add(grid);
		}

		private void UpdateAnomalySliderState()
		{
			var enabled = _strictAnomalies.IsChecked != true;
			foreach(var key in new[] { "unusual", "very", "extreme" }) if(_sliderRows.TryGetValue(key, out var row)) row.IsEnabled = enabled;
		}

		private static FrameworkElement Label(string label, FrameworkElement input) { var panel = new DockPanel(); panel.Children.Add(new TextBlock { Text = label, Width = 165, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }); panel.Children.Add(input); return panel; }
		private static CheckBox Check(string label) => new CheckBox { Content = label, Margin = new Thickness(4) };
		private void Set(string key, double value) { _sliders[key].Value = value; _values[key].Text = value.ToString(CultureInfo.CurrentCulture); }
		private double Get(string key) => _sliders[key].Value;
	}
}
