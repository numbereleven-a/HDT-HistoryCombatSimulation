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
		private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PluginSettings));
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
			HorizontalOffset = NormalizeValue(HorizontalOffset, -500, 500, 14);
			VerticalOffset = NormalizeValue(VerticalOffset, -500, 1200, 155);
			Scale = NormalizeValue(Scale, .5, 2, 1);
			Opacity = NormalizeValue(Opacity, .2, 1, 1);
			BackgroundOpacity = NormalizeValue(BackgroundOpacity, 0, 1, 1);
			VisibleRows = Math.Max(6, Math.Min(20, VisibleRows));
			UnusualExpectedPercent = NormalizeValue(UnusualExpectedPercent, 51, 99, 51);
			VeryUnusualExpectedPercent = NormalizeValue(VeryUnusualExpectedPercent, UnusualExpectedPercent, 99, Math.Max(80, UnusualExpectedPercent));
			ExtremeExpectedPercent = NormalizeValue(ExtremeExpectedPercent, VeryUnusualExpectedPercent, 100, Math.Max(95, VeryUnusualExpectedPercent));
		}

		public AnomalyThresholds GetThresholds() => new AnomalyThresholds(UnusualExpectedPercent / 100, VeryUnusualExpectedPercent / 100, ExtremeExpectedPercent / 100);
		public void CopyFrom(PluginSettings other)
		{
			Enabled = other.Enabled; Layout = other.Layout; Side = other.Side; HorizontalOffset = other.HorizontalOffset; VerticalOffset = other.VerticalOffset;
			Scale = other.Scale; Opacity = other.Opacity; BackgroundOpacity = other.BackgroundOpacity; BackgroundMode = other.BackgroundMode; VisibleRows = other.VisibleRows; UnusualExpectedPercent = other.UnusualExpectedPercent;
			VeryUnusualExpectedPercent = other.VeryUnusualExpectedPercent; ExtremeExpectedPercent = other.ExtremeExpectedPercent; ShowMatchSummary = other.ShowMatchSummary; LockOverlayPosition = other.LockOverlayPosition; HideWhenHearthstoneNotForeground = other.HideWhenHearthstoneNotForeground; ShowAnomalyStatus = other.ShowAnomalyStatus; ShowDamageColumn = other.ShowDamageColumn; ShowHeroColumn = other.ShowHeroColumn; ShowOverlayPreview = other.ShowOverlayPreview; StrictAnomalies = other.StrictAnomalies;
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
				Normalize();
				var path = GetPath();
				var directory = Path.GetDirectoryName(path);
				if(string.IsNullOrEmpty(directory))
					return;
				Directory.CreateDirectory(directory);
				temporaryPath = path + ".tmp";
				using(var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) { Serializer.Serialize(stream, this); stream.Flush(true); }
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
