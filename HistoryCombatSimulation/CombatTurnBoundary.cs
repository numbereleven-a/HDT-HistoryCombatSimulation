namespace HistoryCombatSimulation
{
	public static class CombatTurnBoundary
	{
		public static bool HasAdvanced(int activeTurn, int currentTurn) => activeTurn > 0 && currentTurn > activeTurn;
	}
}
