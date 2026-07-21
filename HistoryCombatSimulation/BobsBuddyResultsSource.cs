using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Controls.Overlay;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace HistoryCombatSimulation
{
	public sealed class BobsBuddyResultsSource : IDisposable
	{
		private BobsBuddyPanel? _panel;
		private int? _activeTurn;
		private readonly BobsBuddyCaptureGate _gate = new BobsBuddyCaptureGate();
		public event EventHandler<SimulationResultEventArgs>? ResultAvailable;

		public bool TryAttach()
		{
			if(_panel != null) return true;
			try
			{
				var panel = Core.Overlay?.FindName("BobsBuddyDisplay") as BobsBuddyPanel;
				if(panel == null) return false;
				_panel = panel; panel.PropertyChanged += OnPropertyChanged; return true;
			}
			catch { return false; }
		}

		public void BeginCombat(int turn, bool guardedRecovery = false)
		{
			_activeTurn = turn; _gate.BeginCombat(guardedRecovery);
		}

		public void PollRecovery() => TryPublishSafely(true);
		public void PollLateRecovery() => TryPublishSafely(true, allowPostCombatState: true);
		public void EnableGuardedRecovery() => _gate.EnableGuardedRecovery();

		public void EndCombat() { _activeTurn = null; _gate.EndCombat(); }
		private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if(_panel == null || !_activeTurn.HasValue) return;
			TryPublishSafely(false);
		}

		private void TryPublishSafely(bool guardedCheck, bool allowPostCombatState = false)
		{
			try { TryPublish(guardedCheck, allowPostCombatState); }
			catch(Exception ex) { Log.Error("History Combat Simulation: Bob's Buddy result read failed (" + ex.GetType().Name + ")."); }
		}

		private void TryPublish(bool guardedCheck, bool allowPostCombatState = false)
		{
			var panel = _panel;
			if(panel == null || !_activeTurn.HasValue)
				return;
			if(!_gate.TryCapture(panel.State.ToString(), panel.ErrorState.ToString(), panel.PercentagesVisibility == Visibility.Visible, panel.WinRateDisplay, panel.TieRateDisplay, panel.LossRateDisplay, CultureInfo.CurrentCulture, out var probabilities, guardedCheck, allowPostCombatState) || probabilities == null)
				return;
			ResultAvailable?.Invoke(this, new SimulationResultEventArgs(_activeTurn.Value, probabilities!));
		}
		public void Dispose()
		{
			if(_panel != null) _panel.PropertyChanged -= OnPropertyChanged;
			_panel = null; EndCombat();
		}
	}

	public sealed class SimulationResultEventArgs : EventArgs
	{
		public SimulationResultEventArgs(int turn, SimulationProbabilities probabilities) { Turn = turn; Probabilities = probabilities; }
		public int Turn { get; }
		public SimulationProbabilities Probabilities { get; }
	}
}
