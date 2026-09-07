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
		private readonly object _sync = new object();
		private BobsBuddyPanel? _panel;
		private int? _activeTurn;
		private long _matchEpoch;
		private long _generation;
		public long Generation { get { lock(_sync) return _generation; } }
		public bool IsCurrent(SimulationResultEventArgs result)
		{
			lock(_sync) return _activeTurn == result.Turn && _matchEpoch == result.MatchEpoch && _generation == result.CaptureGeneration;
		}
		private bool _postCombatRecovery;
		private int _attachMisses;
		private bool _attachFailureLogged;
		private bool _readFailureLogged;
		private readonly BobsBuddyCaptureGate _gate = new BobsBuddyCaptureGate();
		public event EventHandler<SimulationResultEventArgs>? ResultAvailable;

		public bool TryAttach()
		{
			try
			{
				var panel = Core.Overlay?.FindName("BobsBuddyDisplay") as BobsBuddyPanel;
				lock(_sync)
				{
					if(ReferenceEquals(panel, _panel)) return panel != null;
					if(_panel != null) _panel.PropertyChanged -= OnPropertyChanged;
					if(_activeTurn.HasValue) { _generation++; _gate.BeginCombat(true); }
					_panel = null;
					if(panel == null)
					{
						_attachMisses++;
						if(_attachMisses == 5)
							PluginLog.Warn("Bob's Buddy panel is not ready; attachment will continue in the background.");
						return false;
					}
					_panel = panel;
					panel.PropertyChanged += OnPropertyChanged;
					_attachMisses = 0;
					_attachFailureLogged = false;
				}
				return true;
			}
			catch(Exception ex)
			{
				var shouldLog = false;
				lock(_sync)
				{
					if(!_attachFailureLogged) { _attachFailureLogged = true; shouldLog = true; }
				}
				if(shouldLog) PluginLog.Warn("Bob's Buddy panel attachment failed", ex);
				return false;
			}
		}

		public void BeginCombat(int turn, bool guardedRecovery = false, long matchEpoch = 0)
		{
			lock(_sync) { _generation++; _matchEpoch = matchEpoch; _activeTurn = turn; _postCombatRecovery = false; _gate.BeginCombat(guardedRecovery); }
		}

		public void PollRecovery() => TryPublishSafely(true);
		public void BeginPostCombatRecovery() { lock(_sync) { if(_activeTurn.HasValue) _postCombatRecovery = true; } }
		public void PollLateRecovery() { lock(_sync) { if(!_postCombatRecovery) return; } TryPublishSafely(true, allowPostCombatState: true); }
		public void EnableGuardedRecovery() { lock(_sync) _gate.EnableGuardedRecovery(); }

		public void EndCombat() { lock(_sync) EndCombatLocked(); }
		private void EndCombatLocked() { _generation++; _activeTurn = null; _postCombatRecovery = false; _gate.EndCombat(); }
		private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			bool allowPostCombatState;
			lock(_sync)
			{
				if(!ReferenceEquals(sender, _panel) || !_activeTurn.HasValue) return;
				allowPostCombatState = _postCombatRecovery;
			}
			TryPublishSafely(false, allowPostCombatState);
		}

		private void TryPublishSafely(bool guardedCheck, bool allowPostCombatState = false)
		{
			SimulationResultEventArgs? result = null;
			try { lock(_sync) result = TryPublishLocked(guardedCheck, allowPostCombatState); }
			catch(Exception ex)
			{
				lock(_sync)
				{
					if(_readFailureLogged) return;
					_readFailureLogged = true;
				}
				PluginLog.Error("Bob's Buddy result read failed", ex);
			}
			if(result != null) ResultAvailable?.Invoke(this, result);
		}

		private SimulationResultEventArgs? TryPublishLocked(bool guardedCheck, bool allowPostCombatState = false)
		{
			var panel = _panel;
			if(panel == null || !_activeTurn.HasValue)
				return null;
			var state = MapState(panel.State);
			var game = Core.Game;
			if(_postCombatRecovery && game?.IsBattlegroundsCombatPhase == true && CombatTurnBoundary.HasAdvanced(_activeTurn.Value, game.GetTurnNumber())) { EndCombatLocked(); return null; }
			if(!_gate.TryCapture(state, panel.ErrorState == BobsBuddyErrorState.None, panel.PercentagesVisibility == Visibility.Visible, panel.WinRateDisplay, panel.TieRateDisplay, panel.LossRateDisplay, CultureInfo.CurrentCulture, out var probabilities, guardedCheck, allowPostCombatState) || probabilities == null)
				return null;
			_readFailureLogged = false;
			return new SimulationResultEventArgs(_activeTurn.Value, probabilities!, _matchEpoch, _generation);
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
			lock(_sync)
			{
				if(_panel != null) _panel.PropertyChanged -= OnPropertyChanged;
				_panel = null; EndCombatLocked();
				_attachMisses = 0; _attachFailureLogged = false; _readFailureLogged = false;
			}
		}
	}

	public sealed class SimulationResultEventArgs : EventArgs
	{
		public SimulationResultEventArgs(int turn, SimulationProbabilities probabilities, long matchEpoch, long captureGeneration)
		{ Turn = turn; Probabilities = probabilities; MatchEpoch = matchEpoch; CaptureGeneration = captureGeneration; }
		public int Turn { get; }
		public long MatchEpoch { get; }
		public long CaptureGeneration { get; }
		public SimulationProbabilities Probabilities { get; }
	}
}
