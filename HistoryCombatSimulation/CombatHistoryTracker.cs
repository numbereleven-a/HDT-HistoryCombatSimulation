using System;
using System.Collections.Generic;

namespace HistoryCombatSimulation
{
	public sealed class CombatHistoryTracker
	{
		private readonly object _sync = new object();
		private readonly List<CombatRow> _rows = new List<CombatRow>();
		private readonly HashSet<int> _seenTurns = new HashSet<int>();
		private CombatRow? _active;
		private AnomalyThresholds _thresholds = new AnomalyThresholds();
		private bool _strictAnomalies;

		public IReadOnlyList<CombatRow> Rows => SnapshotRows();
		public IReadOnlyList<CombatRow> SnapshotRows()
		{
			lock(_sync)
			{
				var snapshot = new CombatRow[_rows.Count];
				for(var index = 0; index < _rows.Count; index++)
					snapshot[index] = Copy(_rows[index]);
				return snapshot;
			}
		}
		public bool ActiveRowHasSimulation { get { lock(_sync) return _active?.Probabilities != null; } }
		public bool RetainingCompletedMatch { get { lock(_sync) return _retainingCompletedMatch; } }
		private bool _retainingCompletedMatch;
		public event EventHandler? Changed;

		public void SetThresholds(AnomalyThresholds thresholds, bool strictAnomalies = false)
		{
			lock(_sync)
			{
				_thresholds = thresholds; _strictAnomalies = strictAnomalies;
				foreach(var row in _rows)
					row.Anomaly = AnomalyClassifier.Classify(row.Outcome, row.Probabilities, _thresholds, _strictAnomalies);
			}
			OnChanged();
		}

		public CombatRow? BeginCombat(CombatSnapshot snapshot)
		{
			CombatRow row;
			lock(_sync)
			{
				if(snapshot.Turn <= 0)
					return null;
				if(_active != null && _active.Snapshot.Turn == snapshot.Turn)
					return _active;
				if(_seenTurns.Contains(snapshot.Turn))
					return null;
				row = new CombatRow(snapshot);
				_rows.Add(row);
				_seenTurns.Add(snapshot.Turn);
				_active = row;
				_retainingCompletedMatch = false;
			}
			OnChanged();
			return row;
		}

		public bool UpdateSimulation(int turn, SimulationProbabilities probabilities)
		{
			lock(_sync)
			{
				var row = FindTurn(turn);
				if(row == null)
					return false;
				row.Probabilities = probabilities;
				row.Anomaly = AnomalyClassifier.Classify(row.Outcome, probabilities, _thresholds, _strictAnomalies);
			}
			OnChanged();
			return true;
		}

		public bool FinalizeCombat(int turn, CombatOutcome outcome, int? combatDamage = null)
		{
			lock(_sync)
			{
				var row = FindTurn(turn);
				if(row == null || row.IsFinalized)
					return false;
				row.Outcome = outcome;
				row.CombatDamage = combatDamage;
				row.IsFinalized = true;
				row.Anomaly = AnomalyClassifier.Classify(outcome, row.Probabilities, _thresholds, _strictAnomalies);
				if(ReferenceEquals(row, _active)) _active = null;
			}
			OnChanged();
			return true;
		}

		public bool ApplyDefinitiveResult(int turn, CombatOutcome outcome, int? combatDamage = null)
		{
			if(outcome == CombatOutcome.Unknown)
				return false;
			lock(_sync)
			{
				var row = FindTurn(turn);
				if(row == null || !row.IsFinalized || row.Outcome != CombatOutcome.Unknown)
					return false;
				row.Outcome = outcome;
				row.CombatDamage = combatDamage;
				row.Anomaly = AnomalyClassifier.Classify(outcome, row.Probabilities, _thresholds, _strictAnomalies);
			}
			OnChanged();
			return true;
		}

		public bool EndMatch()
		{
			lock(_sync)
			{
				if(_rows.Count == 0 || _retainingCompletedMatch) return false;
				_retainingCompletedMatch = true;
			}
			OnChanged(); return true;
		}

		public void StartNewMatch()
		{
			lock(_sync) { _rows.Clear(); _seenTurns.Clear(); _active = null; _retainingCompletedMatch = false; }
			OnChanged();
		}

		public void Unload() => StartNewMatch();
		private CombatRow? FindTurn(int turn) => _rows.Find(row => row.Snapshot.Turn == turn);
		private static CombatRow Copy(CombatRow row) => new CombatRow(row.Snapshot, row.Identity)
		{
			Probabilities = row.Probabilities,
			Outcome = row.Outcome,
			IsFinalized = row.IsFinalized,
			Anomaly = row.Anomaly,
			CombatDamage = row.CombatDamage
		};
		private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
	}
}
