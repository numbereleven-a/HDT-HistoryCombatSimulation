using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.BobsBuddy;
using Hearthstone_Deck_Tracker.Controls.Overlay;

namespace HistoryCombatSimulation
{
	public sealed class BobsBuddyResultsSource : IDisposable
	{
		private BobsBuddyPanel? _panel;
		private int? _activeTurn;
		private bool _postCombatRecovery;
		private int _attachMisses;
		private bool _attachFailureLogged;
		private bool _readFailureLogged;
		private readonly BobsBuddyCaptureGate _gate = new BobsBuddyCaptureGate();
		public event EventHandler<SimulationResultEventArgs>? ResultAvailable;

		public bool TryAttach()
		{
			if(_panel != null) return true;
			try
			{
				var panel = Core.Overlay?.FindName("BobsBuddyDisplay") as BobsBuddyPanel;
				if(panel == null)
				{
					_attachMisses++;
					if(_attachMisses == 5)
						PluginLog.Warn("Bob's Buddy panel is not ready; attachment will continue in the background.");
					return false;
				}
				_panel = panel; panel.PropertyChanged += OnPropertyChanged; return true;
			}
			catch(Exception ex)
			{
				if(!_attachFailureLogged)
				{
					_attachFailureLogged = true;
					PluginLog.Warn("Bob's Buddy panel attachment failed", ex);
				}
				return false;
			}
		}

		public void BeginCombat(int turn, bool guardedRecovery = false)
		{
			_activeTurn = turn; _postCombatRecovery = false; _gate.BeginCombat(guardedRecovery);
		}

		public void PollRecovery() => TryPublishSafely(true);
		public void BeginPostCombatRecovery() { if(_activeTurn.HasValue) _postCombatRecovery = true; }
		public void PollLateRecovery() { if(_postCombatRecovery) TryPublishSafely(true, allowPostCombatState: true); }
		public void EnableGuardedRecovery() => _gate.EnableGuardedRecovery();

		public void EndCombat() { _activeTurn = null; _postCombatRecovery = false; _gate.EndCombat(); }
		private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if(_panel == null || !_activeTurn.HasValue) return;
			TryPublishSafely(false, _postCombatRecovery);
		}

		private void TryPublishSafely(bool guardedCheck, bool allowPostCombatState = false)
		{
			try { TryPublish(guardedCheck, allowPostCombatState); }
			catch(Exception ex)
			{
				if(_readFailureLogged) return;
				_readFailureLogged = true;
				PluginLog.Error("Bob's Buddy result read failed", ex);
			}
		}

		private void TryPublish(bool guardedCheck, bool allowPostCombatState = false)
		{
			var panel = _panel;
			if(panel == null || !_activeTurn.HasValue)
				return;
			var state = MapState(panel.State);
			var game = Core.Game;
			if(_postCombatRecovery && game?.IsBattlegroundsCombatPhase == true && CombatTurnBoundary.HasAdvanced(_activeTurn.Value, game.GetTurnNumber())) { EndCombat(); return; }
			if(!_gate.TryCapture(state, panel.ErrorState == BobsBuddyErrorState.None, panel.PercentagesVisibility == Visibility.Visible, panel.WinRateDisplay, panel.TieRateDisplay, panel.LossRateDisplay, CultureInfo.CurrentCulture, out var probabilities, guardedCheck, allowPostCombatState) || probabilities == null)
				return;
			_readFailureLogged = false;
			ResultAvailable?.Invoke(this, new SimulationResultEventArgs(_activeTurn.Value, probabilities!));
		}

		private static BobsBuddyCaptureState MapState(BobsBuddyState state)
		{
			switch(state)
			{
				case BobsBuddyState.Combat: return BobsBuddyCaptureState.Combat;
				case BobsBuddyState.Shopping: return BobsBuddyCaptureState.Shopping;
				case BobsBuddyState.GameOver: return BobsBuddyCaptureState.GameOver;
				default: return BobsBuddyCaptureState.Unsupported;
			}
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
