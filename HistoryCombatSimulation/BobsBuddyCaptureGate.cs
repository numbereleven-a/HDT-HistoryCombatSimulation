using System;
using System.Globalization;

namespace HistoryCombatSimulation
{
	public enum BobsBuddyCaptureState
	{
		Unsupported,
		Combat,
		Shopping,
		GameOver
	}

	public sealed class BobsBuddyCaptureGate
	{
		private bool _resetObserved;
		private bool _active;
		private SimulationProbabilities? _lastPublished;
		private bool _guardedRecovery;
		private SimulationProbabilities? _recoveryCandidate;
		private int _stableRecoveryChecks;

		public void BeginCombat(bool guardedRecovery = false) { _active = true; _resetObserved = false; _lastPublished = null; _guardedRecovery = guardedRecovery; _recoveryCandidate = null; _stableRecoveryChecks = 0; }
		public void EnableGuardedRecovery() { _guardedRecovery = _lastPublished == null; _recoveryCandidate = null; _stableRecoveryChecks = 0; }
		public void EndCombat() { BeginCombat(); _active = false; }

		public bool TryCapture(BobsBuddyCaptureState state, bool errorFree, bool percentagesVisible, string? win, string? tie, string? loss, CultureInfo culture, out SimulationProbabilities? result, bool guardedCheck = false, bool allowPostCombatState = false)
		{
			result = null;
			if(!_active) return false;
			var combatState = state == BobsBuddyCaptureState.Combat;
			var postCombatState = state == BobsBuddyCaptureState.Shopping || state == BobsBuddyCaptureState.GameOver;
			var validState = allowPostCombatState ? combatState || postCombatState : combatState;
			if(!validState || !errorFree)
			{ _recoveryCandidate = null; _stableRecoveryChecks = 0; return false; }
			if(IsPlaceholder(win) || IsPlaceholder(tie) || IsPlaceholder(loss))
			{
				// A partial update or a shopping placeholder is not a current combat reset.
				if(combatState && IsPlaceholder(win) && IsPlaceholder(tie) && IsPlaceholder(loss)) _resetObserved = true;
				_recoveryCandidate = null; _stableRecoveryChecks = 0; return false;
			}
			// Repeated values establish stability, never ownership of a new combat.
			if(!_resetObserved) return false;
			if(!percentagesVisible || !BobsBuddyPercentageParser.TryParseTriple(win, tie, loss, culture, out result) || result == null || result.Equals(_lastPublished))
			{
				_recoveryCandidate = null; _stableRecoveryChecks = 0; result = null; return false;
			}
			if(allowPostCombatState || _guardedRecovery)
			{
				if(_recoveryCandidate == null || !result.Equals(_recoveryCandidate)) { _recoveryCandidate = result; _stableRecoveryChecks = 1; result = null; return false; }
				if(!guardedCheck) { result = null; return false; }
				_stableRecoveryChecks++;
				if(_stableRecoveryChecks < 2) { result = null; return false; }
			}
			_lastPublished = result; _guardedRecovery = false; return true;
		}

		private static bool IsPlaceholder(string? value) => string.IsNullOrWhiteSpace(value) || value!.Trim() == "-";
	}
}
