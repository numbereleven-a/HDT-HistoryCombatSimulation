using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;

namespace HistoryCombatSimulation
{
	public sealed class HistoryCombatSimulationPlugin : IPlugin
	{
		private readonly CombatHistoryTracker _tracker = new CombatHistoryTracker();
		private readonly BattlegroundsGameAdapter _game = new BattlegroundsGameAdapter();
		private readonly BobsBuddyResultsSource _bobsBuddy = new BobsBuddyResultsSource();
		private readonly HistoryOverlay _overlay = new HistoryOverlay();
		private readonly Stopwatch _attachRetry = Stopwatch.StartNew();
		private readonly Stopwatch _uptime = Stopwatch.StartNew();
		private readonly Stopwatch _missingSimulationPoll = new Stopwatch();
		private readonly Stopwatch _unknownOutcomePoll = new Stopwatch();
		private readonly Stopwatch _lateSimulationWindow = new Stopwatch();
		private readonly Stopwatch _lateSimulationPoll = new Stopwatch();
		private readonly OutcomeRecoveryGate _outcomeRecoveryGate = new OutcomeRecoveryGate();
		private readonly CombatTurnAdvanceGate _combatTurnAdvanceGate = new CombatTurnAdvanceGate();
		private readonly SoloMatchReentryGate _soloMatchReentryGate = new SoloMatchReentryGate();
		private readonly ReconnectGameStartGate _gameStartGate = new ReconnectGameStartGate();
		private PluginSettings _settings = new PluginSettings();
		private SettingsWindow? _settingsWindow;
		private MenuItem? _menu;
		private MenuItem? _enabledMenuItem;
		private MenuItem? _lockMenuItem;
		private bool _loaded;
		private bool? _wasCombat;
		private bool _insideSoloMatch;
		private long _matchEpoch;
		private CombatSnapshot? _activeSnapshot;
		private bool _activeSnapshotHasReliableStart;
		private bool _observedCombatStart;
		private bool _damageObservedBeforeSnapshot;
		private int _friendlyDamageAmount;
		private int _opponentDamageAmount;
		private int? _awaitingDefinitiveResultTurn;
		private long? _awaitingDefinitiveResultEpoch;
		private OutcomeEvidence? _awaitingDefinitiveEvidence;
		private bool _reconnectRecoveryActive;
		private CombatSnapshot? _pendingUnknownSnapshot;
		private int _pendingFriendlyDamageAmount;
		private int _pendingOpponentDamageAmount;
		private bool _pendingSnapshotStartedMidCombat;
		private int? _lateSimulationTurn;
		private bool? _lastFocusAllowsOverlay;
		private double _lastCanvasWidth = double.NaN;
		private double _lastCanvasHeight = double.NaN;
		private bool _trackingEnabled;
		private bool _waitingForNextGameStart;
		private int? _skippedCombatTurn;
		private bool _updateFailureLogged;

		internal bool IsLoaded => _loaded;

		public string Name => "History Combat Simulation";
		public string Description => "Shows an in-memory Battlegrounds combat history using Bob's Buddy results.\n\nGitHub: https://github.com/numbereleven-a/HDT-HistoryCombatSimulation";
		public string ButtonText => "Settings";
		public string Author => "numbereleven-a";
		public Version Version => PluginVersion.Hdt;
		public MenuItem MenuItem => _menu ??= BuildMenu();

		public void OnLoad()
		{
			_settings = PluginSettings.Load();
			_tracker.SetThresholds(_settings.GetThresholds(), _settings.StrictAnomalies);
			_tracker.Changed += OnHistoryChanged;
			_bobsBuddy.ResultAvailable += OnSimulationResult;
			_overlay.PositionChanged += OnOverlayPositionChanged;
			if(_enabledMenuItem != null) _enabledMenuItem.IsChecked = _settings.Enabled;
			if(_lockMenuItem != null) _lockMenuItem.IsChecked = _settings.LockOverlayPosition;
			_trackingEnabled = _settings.Enabled;
			_loaded = true;
			HdtEventBridge.Attach(this);
			InvokeUi(() => { _overlay.Attach(); _bobsBuddy.TryAttach(); });
		}

		public void OnUnload()
		{
			_loaded = false;
			HdtEventBridge.Detach(this);
			_tracker.Changed -= OnHistoryChanged;
			_bobsBuddy.ResultAvailable -= OnSimulationResult;
			_overlay.PositionChanged -= OnOverlayPositionChanged;
			_bobsBuddy.Dispose();
			_tracker.Unload();
			InvokeUi(() => { _settingsWindow?.Close(); _settingsWindow = null; _overlay.Detach(); });
			_settings.Save();
		}

		public void OnButtonPress() => ShowSettings();

		public void OnUpdate()
		{
			if(!_loaded) return;
			try
			{
				if(_attachRetry.ElapsedMilliseconds >= 2000)
				{
					_attachRetry.Restart();
					InvokeUi(() =>
					{
						if(_overlay.Attach()) RefreshOverlay();
						_bobsBuddy.TryAttach();
					});
				}
				if(!_trackingEnabled || _waitingForNextGameStart) { PumpOverlay(); return; }
				PollLateSimulationRecovery();
				var isSoloBattlegrounds = _game.IsSoloBattlegrounds;
				if(!_soloMatchReentryGate.ShouldTrack(isSoloBattlegrounds))
				{
					PumpOverlay(); return;
				}
				var gameStartDecision = _gameStartGate.Resolve(isSoloBattlegrounds, _game.IsReconnect, _uptime.ElapsedMilliseconds);
				if(gameStartDecision == GameStartDecision.Wait) { PumpOverlay(); return; }
				if(gameStartDecision == GameStartDecision.StartNewMatch || !_insideSoloMatch) StartSoloMatch();

				var combat = _game.IsCombatPhase; var enteredCombat = _wasCombat == false && combat;
				if(enteredCombat) { StopLateSimulationRecovery(); _observedCombatStart = true; _skippedCombatTurn = null; }
				if(combat && _wasCombat != true) ClearUnknownOutcomeRecovery();
				if(_activeSnapshot != null && _combatTurnAdvanceGate.ShouldRollOver(_activeSnapshot.Turn, _game.Turn, combat, _uptime.ElapsedMilliseconds)) FinalizeInterruptedCombat();
				if(combat && _activeSnapshot == null && _skippedCombatTurn != _game.Turn) TryBeginCombat(_observedCombatStart && !_damageObservedBeforeSnapshot);
				if(combat && _activeSnapshot != null && _game.IsReconnect && !_reconnectRecoveryActive) { _reconnectRecoveryActive = true; _missingSimulationPoll.Restart(); InvokeUi(_bobsBuddy.EnableGuardedRecovery); }
				if(combat && _activeSnapshot != null && _tracker.ActiveRow?.Probabilities == null && _missingSimulationPoll.ElapsedMilliseconds >= (_reconnectRecoveryActive ? 10000 : 2000)) { _missingSimulationPoll.Restart(); InvokeUi(_bobsBuddy.PollRecovery); }
				if(_wasCombat == true && !combat) FinalizeActiveCombat();
				if(!combat) TryRecoverUnknownOutcome();
				_wasCombat = combat; _updateFailureLogged = false; PumpOverlay();
			}
			catch(Exception ex)
			{
				if(!_updateFailureLogged)
				{
					_updateFailureLogged = true;
					PluginLog.Error("update failed", ex);
				}
				_lastFocusAllowsOverlay = null; _lastCanvasWidth = double.NaN; _lastCanvasHeight = double.NaN;
				InvokeUi(_overlay.Hide);
			}
		}

		private void StartSoloMatch()
		{
			_matchEpoch++; _insideSoloMatch = true; _wasCombat = null; _activeSnapshot = null; _activeSnapshotHasReliableStart = false; _observedCombatStart = false; _damageObservedBeforeSnapshot = false; _reconnectRecoveryActive = false; _skippedCombatTurn = null; _combatTurnAdvanceGate.Reset(); StopLateSimulationRecovery(); ClearAwaitingDefinitiveResult(); ClearUnknownOutcomeRecovery(); _tracker.StartNewMatch();
		}

		private void TryBeginCombat(bool reliableStart)
		{
			var snapshot = _game.SnapshotCombat(); if(snapshot == null) return;
			var row = _tracker.BeginCombat(snapshot); if(row == null) { _skippedCombatTurn = snapshot.Turn; return; }
			StopLateSimulationRecovery(); ClearUnknownOutcomeRecovery();
			_activeSnapshot = row.Snapshot; _activeSnapshotHasReliableStart = reliableStart; _friendlyDamageAmount = 0; _opponentDamageAmount = 0; ClearAwaitingDefinitiveResult();
			_reconnectRecoveryActive = _game.IsReconnect; _missingSimulationPoll.Restart();
			_bobsBuddy.BeginCombat(row.Snapshot.Turn, guardedRecovery: true);
		}

		private void FinalizeInterruptedCombat()
		{
			var snapshot = _activeSnapshot; if(snapshot == null) return;
			_bobsBuddy.EndCombat(); _tracker.FinalizeCombat(snapshot.Turn, CombatOutcome.Unknown);
			ClearAwaitingDefinitiveResult(); ClearUnknownOutcomeRecovery();
			_activeSnapshot = null; _activeSnapshotHasReliableStart = false; _observedCombatStart = false; _damageObservedBeforeSnapshot = false;
			_friendlyDamageAmount = 0; _opponentDamageAmount = 0; _missingSimulationPoll.Reset(); _reconnectRecoveryActive = false;
		}

		private void FinalizeActiveCombat(CombatOutcome? definitiveMatchResult = null)
		{
			var snapshot = _activeSnapshot;
			if(snapshot == null) return;
			var forceUncertain = !_activeSnapshotHasReliableStart;
			var evidence = _game.GetOutcomeEvidence(snapshot, _friendlyDamageAmount, _opponentDamageAmount, forceUncertain);
			var outcome = CombatOutcomeResolver.Resolve(evidence, definitiveMatchResult);
			var damage = CombatOutcomeResolver.ResolveDamage(outcome, evidence);
			var simulationMissing = _tracker.ActiveRow?.Probabilities == null;
			if(simulationMissing) StartLateSimulationRecovery(snapshot.Turn); else _bobsBuddy.EndCombat();
			_tracker.FinalizeCombat(snapshot.Turn, outcome, damage);
			if(!definitiveMatchResult.HasValue && outcome == CombatOutcome.Unknown)
			{
				_awaitingDefinitiveResultTurn = snapshot.Turn;
				_awaitingDefinitiveResultEpoch = _matchEpoch;
				_awaitingDefinitiveEvidence = evidence;
			}
			else ClearAwaitingDefinitiveResult();
			if(_awaitingDefinitiveResultTurn.HasValue)
			{
				_pendingUnknownSnapshot = snapshot;
				_pendingFriendlyDamageAmount = _friendlyDamageAmount;
				_pendingOpponentDamageAmount = _opponentDamageAmount;
				_pendingSnapshotStartedMidCombat = forceUncertain;
				_outcomeRecoveryGate.Reset();
				_unknownOutcomePoll.Restart();
			}
			else ClearUnknownOutcomeRecovery();
			_activeSnapshot = null;
			_activeSnapshotHasReliableStart = false;
			_observedCombatStart = false;
			_damageObservedBeforeSnapshot = false;
			_friendlyDamageAmount = 0;
			_opponentDamageAmount = 0;
			_missingSimulationPoll.Reset();
			_reconnectRecoveryActive = false;
		}

		private void TryRecoverUnknownOutcome()
		{
			var snapshot = _pendingUnknownSnapshot; if(snapshot == null || _unknownOutcomePoll.ElapsedMilliseconds < 10000) return;
			_unknownOutcomePoll.Restart(); var evidence = _game.GetOutcomeEvidence(snapshot, _pendingFriendlyDamageAmount, _pendingOpponentDamageAmount, _pendingSnapshotStartedMidCombat, allowReconnectRecovery: true); var outcome = CombatOutcomeResolver.Resolve(evidence);
			if(!_outcomeRecoveryGate.TryConfirm(outcome, evidence)) return;
			_tracker.ApplyDefinitiveResult(snapshot.Turn, outcome, CombatOutcomeResolver.ResolveDamage(outcome, evidence)); ClearAwaitingDefinitiveResult(); ClearUnknownOutcomeRecovery();
		}

		private void ClearUnknownOutcomeRecovery()
		{
			_pendingUnknownSnapshot = null; _pendingFriendlyDamageAmount = 0; _pendingOpponentDamageAmount = 0; _pendingSnapshotStartedMidCombat = false; _outcomeRecoveryGate.Reset(); _unknownOutcomePoll.Reset();
		}

		private void ClearAwaitingDefinitiveResult()
		{
			_awaitingDefinitiveResultTurn = null; _awaitingDefinitiveResultEpoch = null; _awaitingDefinitiveEvidence = null;
		}

		private void StartLateSimulationRecovery(int turn)
		{
			_lateSimulationTurn = turn; _lateSimulationWindow.Restart(); _lateSimulationPoll.Restart(); _bobsBuddy.BeginPostCombatRecovery();
		}

		private void PollLateSimulationRecovery()
		{
			if(!_lateSimulationTurn.HasValue) return;
			if(_lateSimulationWindow.ElapsedMilliseconds >= 30000) { StopLateSimulationRecovery(); return; }
			if(_lateSimulationPoll.ElapsedMilliseconds < 2000) return;
			_lateSimulationPoll.Restart(); InvokeUi(_bobsBuddy.PollLateRecovery);
		}

		private void StopLateSimulationRecovery()
		{
			_lateSimulationTurn = null; _lateSimulationWindow.Reset(); _lateSimulationPoll.Reset(); _bobsBuddy.EndCombat();
		}

		internal void HandleGameStart()
		{
			if(_settings.Enabled && _waitingForNextGameStart) { _waitingForNextGameStart = false; _trackingEnabled = true; }
			if(!_trackingEnabled) return;
			_gameStartGate.Notify(_uptime.ElapsedMilliseconds); _soloMatchReentryGate.GameStarted();
		}

		internal void HandleGameEnd()
		{
			if(!_trackingEnabled || _waitingForNextGameStart || !_insideSoloMatch || _gameStartGate.IsPending) return;
			ExitSoloMatch(); _soloMatchReentryGate.MatchEnded();
		}

		private void ExitSoloMatch()
		{
			if(_activeSnapshot != null) FinalizeActiveCombat();
			_insideSoloMatch = false; _wasCombat = null; _observedCombatStart = false; _damageObservedBeforeSnapshot = false; _reconnectRecoveryActive = false; _combatTurnAdvanceGate.Reset(); _missingSimulationPoll.Reset();
			if(!_tracker.EndMatch() && _tracker.SnapshotRows().Count == 0) RefreshOverlay();
		}

		internal void HandleDefinitiveMatchResult(CombatOutcome outcome)
		{
			if(!_trackingEnabled || _waitingForNextGameStart || !_insideSoloMatch || _gameStartGate.IsPending) return;
			if(_activeSnapshot != null) FinalizeActiveCombat(outcome);
			else if(_awaitingDefinitiveResultTurn.HasValue && _awaitingDefinitiveResultEpoch == _matchEpoch && _awaitingDefinitiveEvidence != null)
			{
				if(CombatOutcomeResolver.SupportsDefinitiveMatchResult(outcome, _awaitingDefinitiveEvidence))
					_tracker.ApplyDefinitiveResult(_awaitingDefinitiveResultTurn.Value, outcome, CombatOutcomeResolver.ResolveDamage(outcome, _awaitingDefinitiveEvidence));
			}
			ClearAwaitingDefinitiveResult(); ClearUnknownOutcomeRecovery(); _tracker.EndMatch();
			_soloMatchReentryGate.MatchEnded();
		}

		internal void HandleDamage(PredamageInfo info)
		{
			if(!_trackingEnabled || _waitingForNextGameStart || _gameStartGate.IsPending || info?.Entity == null || info.Value <= 0) return;
			if(_activeSnapshot == null) { if(_insideSoloMatch && _game.IsCombatPhase) _damageObservedBeforeSnapshot = true; return; }
			var target = _game.IdentifyDamageTarget(info.Entity, _activeSnapshot); if(target == DamageTarget.Friendly) _friendlyDamageAmount = Math.Max(_friendlyDamageAmount, info.Value); else if(target == DamageTarget.Opponent) _opponentDamageAmount = Math.Max(_opponentDamageAmount, info.Value);
		}

		private void OnSimulationResult(object? sender, SimulationResultEventArgs e)
		{
			if(!_trackingEnabled || _waitingForNextGameStart) return;
			_tracker.UpdateSimulation(e.Turn, e.Probabilities);
			if(_lateSimulationTurn == e.Turn) StopLateSimulationRecovery();
		}
		private void OnHistoryChanged(object? sender, EventArgs e) => RefreshOverlay();
		private void OnOverlayPositionChanged(object? sender, EventArgs e) { _settingsWindow?.SyncPosition(); _settings.Save(); }
		private void RefreshOverlay()
		{
			var rows = _tracker.SnapshotRows(); var hasContentOrPreview = _insideSoloMatch || _tracker.RetainingCompletedMatch || !_settings.LockOverlayPosition;
			var focusAllowsOverlay = !_settings.HideWhenHearthstoneNotForeground || User32.IsHearthstoneInForeground();
			_lastFocusAllowsOverlay = focusAllowsOverlay; InvokeUi(() => _overlay.Update(rows, _settings, hasContentOrPreview && focusAllowsOverlay));
		}

		private void PumpOverlay()
		{
			var focusAllowsOverlay = !_settings.HideWhenHearthstoneNotForeground || User32.IsHearthstoneInForeground();
			var canvas = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas;
			if(canvas == null) { _lastCanvasWidth = double.NaN; _lastCanvasHeight = double.NaN; return; }
			var width = canvas.ActualWidth; var height = canvas.ActualHeight;
			if(_lastFocusAllowsOverlay == focusAllowsOverlay && Math.Abs(_lastCanvasWidth - width) < .1 && Math.Abs(_lastCanvasHeight - height) < .1) return;
			_lastFocusAllowsOverlay = focusAllowsOverlay; _lastCanvasWidth = width; _lastCanvasHeight = height; RefreshOverlay();
		}

		private MenuItem BuildMenu()
		{
			var root = new MenuItem { Header = "History Combat Simulation" };
			_enabledMenuItem = new MenuItem { Header = "Enabled", IsCheckable = true, IsChecked = _settings.Enabled }; _enabledMenuItem.Click += (_, __) => { _settings.Enabled = _enabledMenuItem.IsChecked; ApplyEnabledState(); _settings.Save(); RefreshOverlay(); };
			var settings = new MenuItem { Header = "Settings" }; settings.Click += (_, __) => ShowSettings();
			_lockMenuItem = new MenuItem { Header = "Lock overlay position", IsCheckable = true, IsChecked = _settings.LockOverlayPosition }; _lockMenuItem.Click += (_, __) => { _settings.LockOverlayPosition = _lockMenuItem.IsChecked; _settings.Save(); RefreshOverlay(); };
			root.Items.Add(_enabledMenuItem); root.Items.Add(_lockMenuItem); root.Items.Add(settings); return root;
		}

		private void ShowSettings() => InvokeUi(() => { if(_settingsWindow != null) { _settingsWindow.Activate(); return; } _settingsWindow = new SettingsWindow(_settings, ApplySettings, Version); _settingsWindow.Closed += (_, __) => _settingsWindow = null; _settingsWindow.Show(); });
		private void ApplySettings()
		{
			_settings.Normalize();
			ApplyEnabledState();
			_settings.Save();
			if(_enabledMenuItem != null) _enabledMenuItem.IsChecked = _settings.Enabled;
			if(_lockMenuItem != null) _lockMenuItem.IsChecked = _settings.LockOverlayPosition;
			_tracker.SetThresholds(_settings.GetThresholds(), _settings.StrictAnomalies);
			RefreshOverlay();
		}

		private void ApplyEnabledState()
		{
			if(_settings.Enabled == _trackingEnabled && !_waitingForNextGameStart) return;
			if(!_settings.Enabled)
			{
				_trackingEnabled = false; _waitingForNextGameStart = false;
				if(_activeSnapshot != null) FinalizeInterruptedCombat();
				StopLateSimulationRecovery(); ClearUnknownOutcomeRecovery(); ClearAwaitingDefinitiveResult();
				_wasCombat = null; _insideSoloMatch = false; _skippedCombatTurn = null;
				return;
			}
			_trackingEnabled = false; _waitingForNextGameStart = true;
		}
		private static void InvokeUi(Action action)
		{
			Action safeAction = () =>
			{
				try { action(); }
				catch(Exception ex) { PluginLog.Error("UI action failed", ex); }
			};
			var dispatcher = Application.Current?.Dispatcher;
			if(dispatcher == null || dispatcher.CheckAccess()) safeAction();
			else dispatcher.BeginInvoke(safeAction);
		}
	}
}
