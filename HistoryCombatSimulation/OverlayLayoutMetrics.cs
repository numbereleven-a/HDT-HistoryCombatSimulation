namespace HistoryCombatSimulation
{
	public sealed class OverlayLayoutMetrics
	{
		private OverlayLayoutMetrics(double padding, double titleFontSize, double collapseWidth, double collapseHeight,
			double headerHeight, double headerFontSize, double rowHeight, double rowFontSize, double portraitSize,
			double turnWidth, double heroWidth, double probabilityWidth, double damageWidth, double resultWidth,
			double outcomeFontSize, double anomalyFontSize)
		{
			Padding = padding; TitleFontSize = titleFontSize; CollapseWidth = collapseWidth; CollapseHeight = collapseHeight;
			HeaderHeight = headerHeight; HeaderFontSize = headerFontSize; RowHeight = rowHeight; RowFontSize = rowFontSize;
			PortraitSize = portraitSize; TurnWidth = turnWidth; HeroWidth = heroWidth; ProbabilityWidth = probabilityWidth;
			DamageWidth = damageWidth; ResultWidth = resultWidth; OutcomeFontSize = outcomeFontSize; AnomalyFontSize = anomalyFontSize;
		}

		public double Padding { get; }
		public double TitleFontSize { get; }
		public double CollapseWidth { get; }
		public double CollapseHeight { get; }
		public double HeaderHeight { get; }
		public double HeaderFontSize { get; }
		public double RowHeight { get; }
		public double RowFontSize { get; }
		public double PortraitSize { get; }
		public double TurnWidth { get; }
		public double HeroWidth { get; }
		public double ProbabilityWidth { get; }
		public double DamageWidth { get; }
		public double ResultWidth { get; }
		public double OutcomeFontSize { get; }
		public double AnomalyFontSize { get; }

		public double CompactExpandedWidth(bool showHero, bool showDamage) =>
			Padding * 2 + TurnWidth + (showHero ? HeroWidth : 0) + ProbabilityWidth + (showDamage ? DamageWidth : 0) + ResultWidth;

		public double CompactCollapsedWidth => Padding * 2 + CollapseWidth;

		public static OverlayLayoutMetrics For(HistoryLayout layout) => layout == HistoryLayout.Compact
			? new OverlayLayoutMetrics(3, 11, 14, 12, 12, 8, 20, 9, 19, 18, 24, 58, 28, 42, 9, 8)
			: new OverlayLayoutMetrics(7, 13, 21, 18, 18, 9, 31, 12, 29, 24, 38, 42, 40, 62, 12, 10);
	}
}
