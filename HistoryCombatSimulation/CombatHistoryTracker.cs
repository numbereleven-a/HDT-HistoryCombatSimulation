using System;
using System.Collections.Generic;

namespace HistoryCombatSimulation
{
	public sealed class CombatHistoryTracker
	{
		private readonly List<CombatRow> _rows = new List<CombatRow>();
		private readonly HashSet<int> _seenTurns = new HashSet<int>();
		private CombatRow? _active;
		private AnomalyThresholds _thresholds = new AnomalyThresholds();
		private bool _strictAnomalies;

		public IReadOnlyList<CombatRow> Rows => _rows;
		public IReadOnlyList<CombatRow> SnapshotRows() => _rows.ToArray();
		public CombatRow? ActiveRow => _active;
		public bool RetainingCompletedMatch { get; private set; }
		public event EventHandler? Changed;

		public void SetThresholds(AnomalyThresholds thresholds, bool strictAnomalies = false)
		{
			_thresholds = thresholds; _strictAnomalies = strictAnomalies;
			foreach(var row in _rows)
				row.Anomaly = AnomalyClassifier.Classify(row.Outcome, row.Probabilities, _thresholds, _strictAnomalies);
			OnChanged();
		}

		public CombatRow? BeginCombat(CombatSnapshot snapshot)
		{
			if(snapshot.Turn <= 0)
				return null;
			if(_active != null && _active.Snapshot.Turn == snapshot.Turn)
				return _active;
			if(_seenTurns.Contains(snapshot.Turn))
				return null;
			var row = new CombatRow(snapshot);
			_rows.Add(row);
			_seenTurns.Add(snapshot.Turn);
			_active = row;
			RetainingCompletedMatch = false;
			OnChanged();
			return row;
		}

		public bool UpdateSimulation(int turn, SimulationProbabilities probabilities)
		{
			var row = FindTurn(turn);
			if(row == null)
				return false;
			row.Probabilities = probabilities;
			row.Anomaly = AnomalyClassifier.Classify(row.Outcome, probabilities, _thresholds, _strictAnomalies);
			OnChanged();
			return true;
		}

		public bool FinalizeCombat(int turn, CombatOutcome outcome, int? combatDamage = null)
		{
			var row = FindTurn(turn);
			if(row == null || row.IsFinalized)
				return false;
			row.Outcome = outcome;
			row.CombatDamage = combatDamage;
			row.IsFinalized = true;
			row.Anomaly = AnomalyClassifier.Classify(outcome, row.Probabilities, _thresholds, _strictAnomalies);
			if(ReferenceEquals(row, _active)) _active = null;
			OnChanged();
			return true;
		}

		public bool ApplyDefinitiveResult(int turn, CombatOutcome outcome, int? combatDamage = null)
		{
			if(outcome == CombatOutcome.Unknown)
				return false;
			var row = FindTurn(turn);
			if(row == null || !row.IsFinalized || row.Outcome != CombatOutcome.Unknown)
				return false;
			row.Outcome = outcome;
			row.CombatDamage = combatDamage;
			row.Anomaly = AnomalyClassifier.Classify(outcome, row.Probabilities, _thresholds, _strictAnomalies);
			OnChanged();
			return true;
		}

		public bool EndMatch()
		{
			if(_rows.Count == 0 || RetainingCompletedMatch) return false;
			RetainingCompletedMatch = true; OnChanged(); return true;
		}

		public void StartNewMatch()
		{
			_rows.Clear(); _seenTurns.Clear(); _active = null; RetainingCompletedMatch = false; OnChanged();
		}

		public void Unload() => StartNewMatch();
		private CombatRow? FindTurn(int turn) => _rows.Find(row => row.Snapshot.Turn == turn);
		private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
	}
}
