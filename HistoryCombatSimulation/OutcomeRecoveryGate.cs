namespace HistoryCombatSimulation
{
	public sealed class OutcomeRecoveryGate
	{
		private OutcomeEvidence? _candidate;
		private CombatOutcome _candidateOutcome;
		private int _stableChecks;

		public void Reset() { _candidate = null; _candidateOutcome = CombatOutcome.Unknown; _stableChecks = 0; }

		public bool TryConfirm(CombatOutcome outcome, OutcomeEvidence evidence)
		{
			if(outcome == CombatOutcome.Unknown) { Reset(); return false; }
			if(_candidate == null || _candidateOutcome != outcome || !SameEvidence(_candidate, evidence))
			{
				_candidate = evidence; _candidateOutcome = outcome; _stableChecks = 1; return false;
			}
			_stableChecks++;
			return _stableChecks >= 2;
		}

		private static bool SameEvidence(OutcomeEvidence left, OutcomeEvidence right) =>
			left.FriendlyBefore == right.FriendlyBefore && left.FriendlyAfter == right.FriendlyAfter
			&& left.OpponentBefore == right.OpponentBefore && left.OpponentAfter == right.OpponentAfter
			&& left.FriendlyDamageObserved == right.FriendlyDamageObserved && left.OpponentDamageObserved == right.OpponentDamageObserved
			&& left.UncertainReconnect == right.UncertainReconnect
			&& left.FriendlyDamageAmount == right.FriendlyDamageAmount && left.OpponentDamageAmount == right.OpponentDamageAmount;
	}
}
