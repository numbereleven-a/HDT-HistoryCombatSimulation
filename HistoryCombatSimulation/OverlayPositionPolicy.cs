namespace HistoryCombatSimulation
{
	public static class OverlayPositionPolicy
	{
		public static double HorizontalOffsetFromLeft(OverlaySide side, double canvasWidth, double scaledOverlayWidth, double left) =>
			side == OverlaySide.Left ? left : canvasWidth - scaledOverlayWidth - left;

		public static bool PreserveExpandedWidthWhenCollapsed(HistoryLayout layout) => layout == HistoryLayout.Normal;

		public static double CompactLeftAfterToggle(double currentLeft, double expandedWidth, double collapsedWidth, double scale, bool collapsing) =>
			currentLeft + (collapsing ? 1 : -1) * (expandedWidth - collapsedWidth) * scale;

		public static double CompactDragHorizontalOffset(OverlaySide side, double canvasWidth, double expandedWidth, double expandedExtraWidth, double collapsedWidth, double scale, double collapsedLeft)
		{
			var expandedLeft = CompactLeftAfterToggle(collapsedLeft, expandedWidth, collapsedWidth, scale, collapsing: false);
			return side == OverlaySide.Left
				? expandedLeft
				: HorizontalOffsetFromLeft(side, canvasWidth, (expandedWidth + expandedExtraWidth) * scale, expandedLeft);
		}

		public static double CompactCollapsedLeftFromHorizontalOffset(OverlaySide side, double canvasWidth, double expandedWidth, double expandedExtraWidth, double collapsedWidth, double scale, double horizontalOffset)
		{
			var expandedLeft = side == OverlaySide.Left
				? horizontalOffset
				: canvasWidth - (expandedWidth + expandedExtraWidth) * scale - horizontalOffset;
			return CompactLeftAfterToggle(expandedLeft, expandedWidth, collapsedWidth, scale, collapsing: true);
		}
	}
}
