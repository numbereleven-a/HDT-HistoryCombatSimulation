namespace HistoryCombatSimulation
{
	public static class CombatTurnBoundary
	{
		public static bool HasAdvanced(int activeTurn, int currentTurn) => activeTurn > 0 && currentTurn > activeTurn;
	}

	public sealed class CombatTurnAdvanceGate
	{
		public const long ConfirmationMilliseconds = 2000;
		private int? _candidateTurn;
		private long _candidateSince;

		public bool ShouldRollOver(int activeTurn, int currentTurn, bool isCombatPhase, long nowMilliseconds)
		{
			if(!isCombatPhase || !CombatTurnBoundary.HasAdvanced(activeTurn, currentTurn))
			{
				Reset();
				return false;
			}
			if(_candidateTurn != currentTurn || nowMilliseconds < _candidateSince)
			{
				_candidateTurn = currentTurn;
				_candidateSince = nowMilliseconds;
				return false;
			}
			if(nowMilliseconds - _candidateSince < ConfirmationMilliseconds)
				return false;
			Reset();
			return true;
		}

		public void Reset()
		{
			_candidateTurn = null;
			_candidateSince = 0;
		}
	}
}
