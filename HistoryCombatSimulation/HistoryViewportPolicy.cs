namespace HistoryCombatSimulation
{
	public static class HistoryViewportPolicy
	{
		public static bool ShouldScrollToNewest(int previousRowCount, int rowCount, int? previousVisibleRows, int visibleRows, bool wasAtNewest) =>
			rowCount > previousRowCount || wasAtNewest && previousVisibleRows.HasValue && previousVisibleRows.Value != visibleRows;
	}
}
