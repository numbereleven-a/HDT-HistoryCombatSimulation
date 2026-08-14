using System;
using System.IO;
using System.Xml.Serialization;
using Hearthstone_Deck_Tracker;

namespace HistoryCombatSimulation
{
	public enum HistoryLayout { Normal, Compact }
	public enum OverlaySide { Left, Right }
	public enum OverlayBackgroundMode { Full, Header, Transparent }

	public sealed class PluginSettings
	{
		public const double MinimumPositionOffset = -10000;
		public const double MaximumPositionOffset = 10000;
		private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PluginSettings));
		private readonly object _sync = new object();
		public bool Enabled { get; set; } = true;
		public HistoryLayout Layout { get; set; } = HistoryLayout.Normal;
		public OverlaySide Side { get; set; } = OverlaySide.Right;
		public double HorizontalOffset { get; set; } = 14;
		public double VerticalOffset { get; set; } = 155;
		public double Scale { get; set; } = 1;
		public double Opacity { get; set; } = 1;
		public double BackgroundOpacity { get; set; } = 1;
		public OverlayBackgroundMode BackgroundMode { get; set; } = OverlayBackgroundMode.Full;
		public int VisibleRows { get; set; } = 14;
		public double UnusualExpectedPercent { get; set; } = 51;
		public double VeryUnusualExpectedPercent { get; set; } = 80;
		public double ExtremeExpectedPercent { get; set; } = 95;
		public bool ShowMatchSummary { get; set; }
		public bool LockOverlayPosition { get; set; } = true;
		public bool HideWhenHearthstoneNotForeground { get; set; }
		public bool ShowAnomalyStatus { get; set; } = true;
		public bool ShowDamageColumn { get; set; }
		public bool ShowHeroColumn { get; set; } = true;
		public bool ShowOverlayPreview { get; set; }
		public bool StrictAnomalies { get; set; } = true;

		public void Normalize()
		{
			lock(_sync)
			{
				HorizontalOffset = NormalizeValue(HorizontalOffset, MinimumPositionOffset, MaximumPositionOffset, 14);
				VerticalOffset = NormalizeValue(VerticalOffset, MinimumPositionOffset, MaximumPositionOffset, 155);
				Scale = NormalizeValue(Scale, .5, 2, 1);
				Opacity = NormalizeValue(Opacity, .2, 1, 1);
				BackgroundOpacity = NormalizeValue(BackgroundOpacity, 0, 1, 1);
				VisibleRows = Math.Max(6, Math.Min(20, VisibleRows));
				UnusualExpectedPercent = NormalizeValue(UnusualExpectedPercent, 51, 99, 51);
				VeryUnusualExpectedPercent = NormalizeValue(VeryUnusualExpectedPercent, UnusualExpectedPercent, 99, Math.Max(80, UnusualExpectedPercent));
				ExtremeExpectedPercent = NormalizeValue(ExtremeExpectedPercent, VeryUnusualExpectedPercent, 100, Math.Max(95, VeryUnusualExpectedPercent));
			}
		}

		public AnomalyThresholds GetThresholds() { lock(_sync) return new AnomalyThresholds(UnusualExpectedPercent / 100, VeryUnusualExpectedPercent / 100, ExtremeExpectedPercent / 100); }

		public void SetPosition(double horizontalOffset, double verticalOffset)
		{
			lock(_sync) { HorizontalOffset = horizontalOffset; VerticalOffset = verticalOffset; }
		}

		public PluginSettings Snapshot()
		{
			lock(_sync)
			{
				return new PluginSettings
				{
					Enabled = Enabled, Layout = Layout, Side = Side, HorizontalOffset = HorizontalOffset, VerticalOffset = VerticalOffset,
					Scale = Scale, Opacity = Opacity, BackgroundOpacity = BackgroundOpacity, BackgroundMode = BackgroundMode,
					VisibleRows = VisibleRows, UnusualExpectedPercent = UnusualExpectedPercent, VeryUnusualExpectedPercent = VeryUnusualExpectedPercent,
					ExtremeExpectedPercent = ExtremeExpectedPercent, ShowMatchSummary = ShowMatchSummary,
					LockOverlayPosition = LockOverlayPosition, HideWhenHearthstoneNotForeground = HideWhenHearthstoneNotForeground,
					ShowAnomalyStatus = ShowAnomalyStatus, ShowDamageColumn = ShowDamageColumn, ShowHeroColumn = ShowHeroColumn,
					ShowOverlayPreview = ShowOverlayPreview, StrictAnomalies = StrictAnomalies
				};
			}
		}

		public void CopyFrom(PluginSettings other)
		{
			var source = other.Snapshot();
			lock(_sync)
			{
				Enabled = source.Enabled; Layout = source.Layout; Side = source.Side; HorizontalOffset = source.HorizontalOffset; VerticalOffset = source.VerticalOffset;
				Scale = source.Scale; Opacity = source.Opacity; BackgroundOpacity = source.BackgroundOpacity; BackgroundMode = source.BackgroundMode; VisibleRows = source.VisibleRows; UnusualExpectedPercent = source.UnusualExpectedPercent;
				VeryUnusualExpectedPercent = source.VeryUnusualExpectedPercent; ExtremeExpectedPercent = source.ExtremeExpectedPercent; ShowMatchSummary = source.ShowMatchSummary; LockOverlayPosition = source.LockOverlayPosition; HideWhenHearthstoneNotForeground = source.HideWhenHearthstoneNotForeground; ShowAnomalyStatus = source.ShowAnomalyStatus; ShowDamageColumn = source.ShowDamageColumn; ShowHeroColumn = source.ShowHeroColumn; ShowOverlayPreview = source.ShowOverlayPreview; StrictAnomalies = source.StrictAnomalies;
			}
		}

		public static PluginSettings Load()
		{
			try
			{
				var path = GetPath();
				if(File.Exists(path))
					using(var stream = File.OpenRead(path))
					{
						var loaded = Serializer.Deserialize(stream) as PluginSettings ?? new PluginSettings(); loaded.Normalize(); return loaded;
					}
			}
			catch(Exception ex)
			{
				PluginLog.Warn("settings load failed; defaults will be used", ex);
			}
			return new PluginSettings();
		}

		public void Save()
		{
			string? temporaryPath = null;
			try
			{
				var snapshot = Snapshot();
				snapshot.Normalize();
				var path = GetPath();
				var directory = Path.GetDirectoryName(path);
				if(string.IsNullOrEmpty(directory))
					return;
				Directory.CreateDirectory(directory);
				temporaryPath = path + ".tmp";
				using(var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) { Serializer.Serialize(stream, snapshot); stream.Flush(true); }
				if(File.Exists(path)) File.Replace(temporaryPath, path, null); else File.Move(temporaryPath, path);
			}
			catch(Exception ex)
			{
				PluginLog.Error("settings save failed", ex);
				if(temporaryPath != null)
					try { File.Delete(temporaryPath); }
					catch { }
			}
		}

		private static string GetPath() => Path.Combine(Config.Instance.ConfigDir, "HistoryCombatSimulation", "settings.xml");
		private static double NormalizeValue(double value, double min, double max, double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
	}
}
