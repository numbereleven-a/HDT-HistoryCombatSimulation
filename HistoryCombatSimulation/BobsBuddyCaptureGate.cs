using System;
using System.Globalization;

namespace HistoryCombatSimulation
{
	public sealed class BobsBuddyCaptureGate
	{
		private bool _resetObserved;
		private SimulationProbabilities? _lastPublished;
		private bool _guardedRecovery;
		private SimulationProbabilities? _recoveryCandidate;
		private int _stableRecoveryChecks;

		public void BeginCombat(bool guardedRecovery = false) { _resetObserved = false; _lastPublished = null; _guardedRecovery = guardedRecovery; _recoveryCandidate = null; _stableRecoveryChecks = 0; }
		public void EnableGuardedRecovery() { _guardedRecovery = true; _recoveryCandidate = null; _stableRecoveryChecks = 0; }
		public void EndCombat() => BeginCombat();

		public bool TryCapture(string state, string errorState, bool percentagesVisible, string? win, string? tie, string? loss, CultureInfo culture, out SimulationProbabilities? result, bool guardedCheck = false, bool allowPostCombatState = false)
		{
			result = null;
			var validState = string.Equals(state, "Combat", StringComparison.Ordinal)
				|| allowPostCombatState && (string.Equals(state, "Shopping", StringComparison.Ordinal) || string.Equals(state, "GameOver", StringComparison.Ordinal));
			if(!validState || !string.Equals(errorState, "None", StringComparison.Ordinal))
				return false;
			if(IsPlaceholder(win) || IsPlaceholder(tie) || IsPlaceholder(loss))
			{
				_resetObserved = true; _recoveryCandidate = null; _stableRecoveryChecks = 0; return false;
			}
			if(!percentagesVisible || !BobsBuddyPercentageParser.TryParseTriple(win, tie, loss, culture, out result) || result == null || result.Equals(_lastPublished))
			{
				result = null; return false;
			}
			if(!_resetObserved)
			{
				if(!_guardedRecovery) { result = null; return false; }
				if(_recoveryCandidate == null || !result.Equals(_recoveryCandidate)) { _recoveryCandidate = result; _stableRecoveryChecks = 1; result = null; return false; }
				if(!guardedCheck) { result = null; return false; }
				_stableRecoveryChecks++;
				if(_stableRecoveryChecks < 2) { result = null; return false; }
				_resetObserved = true;
			}
			_lastPublished = result; return true;
		}

		private static bool IsPlaceholder(string? value) => string.IsNullOrWhiteSpace(value) || value!.Trim() == "-";
	}
}
