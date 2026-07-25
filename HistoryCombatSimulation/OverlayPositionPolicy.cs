namespace HistoryCombatSimulation
{
	public static class OverlayPositionPolicy
	{
		public static double HorizontalOffsetFromLeft(OverlaySide side, double canvasWidth, double scaledOverlayWidth, double left) =>
			side == OverlaySide.Left ? left : canvasWidth - scaledOverlayWidth - left;
	}
}
