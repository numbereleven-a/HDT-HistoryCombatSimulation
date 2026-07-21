namespace HistoryCombatSimulation
{
	public static class CombatOutcomeResolver
	{
		public static CombatOutcome Resolve(OutcomeEvidence evidence, CombatOutcome? definitiveMatchResult = null)
		{
			if(definitiveMatchResult.HasValue && definitiveMatchResult.Value != CombatOutcome.Unknown)
				return definitiveMatchResult.Value;
			if(evidence.FriendlyDamageObserved && evidence.OpponentDamageObserved)
				return CombatOutcome.Unknown;
			if(evidence.FriendlyDamageObserved)
				return CombatOutcome.Loss;
			if(evidence.OpponentDamageObserved)
				return CombatOutcome.Win;
			if(evidence.UncertainReconnect)
				return CombatOutcome.Unknown;
			var friendlyChanged = Decreased(evidence.FriendlyBefore, evidence.FriendlyAfter);
			var opponentChanged = Decreased(evidence.OpponentBefore, evidence.OpponentAfter);
			if(friendlyChanged && opponentChanged)
				return CombatOutcome.Unknown;
			if(friendlyChanged)
				return CombatOutcome.Loss;
			if(opponentChanged)
				return CombatOutcome.Win;
			if(AllPresent(evidence))
				return CombatOutcome.Tie;
			return CombatOutcome.Unknown;
		}

		public static int? ResolveDamage(CombatOutcome outcome, OutcomeEvidence evidence)
		{
			if(outcome == CombatOutcome.Tie)
				return 0;
			if(outcome == CombatOutcome.Win)
			{
				var damage = ObservedOrDecrease(evidence.OpponentDamageAmount, evidence.OpponentBefore, evidence.OpponentAfter);
				return damage.HasValue ? damage.Value : (int?)null;
			}
			if(outcome == CombatOutcome.Loss)
			{
				var damage = ObservedOrDecrease(evidence.FriendlyDamageAmount, evidence.FriendlyBefore, evidence.FriendlyAfter);
				return damage.HasValue ? -damage.Value : (int?)null;
			}
			return null;
		}

		public static bool SupportsDefinitiveMatchResult(CombatOutcome outcome, OutcomeEvidence evidence)
		{
			var friendlyDead = evidence.FriendlyAfter.HasValue && evidence.FriendlyAfter.Value <= 0
				|| evidence.FriendlyBefore.HasValue && evidence.FriendlyDamageAmount > 0 && evidence.FriendlyDamageAmount >= evidence.FriendlyBefore.Value;
			var opponentDead = evidence.OpponentAfter.HasValue && evidence.OpponentAfter.Value <= 0
				|| evidence.OpponentBefore.HasValue && evidence.OpponentDamageAmount > 0 && evidence.OpponentDamageAmount >= evidence.OpponentBefore.Value;
			return outcome == CombatOutcome.Loss && friendlyDead && !opponentDead
				|| outcome == CombatOutcome.Win && opponentDead && !friendlyDead;
		}

		private static bool Decreased(int? before, int? after) => before.HasValue && after.HasValue && after.Value < before.Value;
		private static int? ObservedOrDecrease(int observed, int? before, int? after) => observed > 0 ? observed : Decreased(before, after) ? before!.Value - after!.Value : (int?)null;
		private static bool AllPresent(OutcomeEvidence e) => e.FriendlyBefore.HasValue && e.FriendlyAfter.HasValue && e.OpponentBefore.HasValue && e.OpponentAfter.HasValue;
	}
}
