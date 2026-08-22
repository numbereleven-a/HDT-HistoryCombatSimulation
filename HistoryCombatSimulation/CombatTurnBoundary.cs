namespace HistoryCombatSimulation
{
	public static class CombatTurnBoundary
	{
		public static bool HasAdvanced(int activeTurn, int currentTurn) => activeTurn > 0 && currentTurn > activeTurn;
	}

	public enum LateSimulationRecoveryAction { None, Poll, Stop }

	public static class LateSimulationRecoveryPolicy
	{
		public static LateSimulationRecoveryAction Decide(bool hasBinding, bool gameStartPending, bool? wasCombat, bool isCombatPhase)
		{
			if(!hasBinding || gameStartPending)
				return LateSimulationRecoveryAction.None;
			return wasCombat == false && isCombatPhase
				? LateSimulationRecoveryAction.Stop
				: LateSimulationRecoveryAction.Poll;
		}
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
