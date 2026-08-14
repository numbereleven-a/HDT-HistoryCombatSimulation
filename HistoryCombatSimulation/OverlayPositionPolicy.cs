namespace HistoryCombatSimulation
{
	public static class OverlayPositionPolicy
	{
		public static double HorizontalOffsetFromLeft(OverlaySide side, double canvasWidth, double scaledOverlayWidth, double left) =>
			side == OverlaySide.Left ? left : canvasWidth - scaledOverlayWidth - left;

		public static bool PreserveExpandedWidthWhenCollapsed(HistoryLayout layout) => layout == HistoryLayout.Normal;

		public static double CompactLeftAfterToggle(double currentLeft, double expandedWidth, double collapsedWidth, double scale, bool collapsing) =>
			currentLeft + (collapsing ? 1 : -1) * (expandedWidth - collapsedWidth) * scale;
	}
}
