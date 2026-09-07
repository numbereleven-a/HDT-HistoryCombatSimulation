using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Serialization;
using HistoryCombatSimulation;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		var runtime = Environment.GetEnvironmentVariable("HDT_TEST_RUNTIME");
		if(!string.IsNullOrEmpty(runtime)) AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
		{
			var name = new AssemblyName(e.Name).Name;
			foreach(var extension in new[] { ".dll", ".exe" })
			{
				var path = Path.Combine(runtime, name + extension);
				if(File.Exists(path)) return Assembly.LoadFrom(path);
			}
			return null;
		};
		if(args.Contains("--lifecycle"))
		{
			var failures = 0;
			foreach(var test in new Action[] { CombatLifecycleTests.StableOldPercentagesRemainUnknown, CombatLifecycleTests.RecoveryRequiresResetAndContinuousCompleteValues, CombatLifecycleTests.UnloadClearsCombatState, CombatLifecycleTests.SimulationEventsCannotCrossBindings, CombatLifecycleTests.QueuedUiWorkCannotCrossReload })
				try { test(); Console.WriteLine("PASS " + test.Method.Name); }
				catch(Exception ex) { failures++; Console.WriteLine("FAIL " + test.Method.Name + ": " + ex); }
			return failures == 0 ? 0 : 1;
		}
		OneRowPerCombatAndDuplicateNotifications();
		TrackerAndSettingsSnapshotsAreThreadSafe();
		RerunUpdatesTheSameRow();
		StaleDataDoesNotLeak();
		ParserAcceptsCulturesAndRejectsIncompleteValues();
		CaptureGateRejectsStalePartialAndErrorStates();
		PostCombatRecoveryAcceptsStableCombatOrShoppingData();
		OutcomeResolutionIsConservative();
		InterruptedCombatKeepsObservedDamage();
		OutcomeRecoveryRequiresStableEvidence();
		GhostIdentityIsKeptWithoutGuessing();
		ReconnectDuplicateIsIgnored();
		PostMatchRetentionAndNewMatchReset();
		PostMatchStaleSoloStateDoesNotStartANewMatch();
		ReconnectGameStartDoesNotClearTheExistingMatch();
		TransientTurnAdvanceDoesNotCreateAPhantomCombat();
		StableTurnAdvanceRecoversASkippedPhaseEdge();
		LateSimulationBindingStopsAtTheNextCombat();
		FormattingSupportsBothLayouts();
		LongHistoryIsRetainedForViewporting();
		ViewportChangesKeepTheNewestRowsVisible();
		OverlayPositionRemainsStableWhenWidthChanges();
		AnomaliesAndSummaryAreSymmetric();
		VersionParsingAndManualUpdateCheck();
		VersionAndMovementDefaultsAreStable();
		SettingsOpenWithoutInitializedOwnerHandle();
		Console.WriteLine("All HistoryCombatSimulation tests passed.");
		return 0;
	}

	private static void OneRowPerCombatAndDuplicateNotifications()
	{
		var tracker = new CombatHistoryTracker();
		var first = tracker.BeginCombat(Snapshot(5, 2));
		var duplicate = tracker.BeginCombat(Snapshot(5, 2));
		Same(first, duplicate, "duplicate phase transition returns active row");
		Equal(1, tracker.Rows.Count, "one row per turn");
		True(tracker.FinalizeCombat(5, CombatOutcome.Win), "first finalization");
		False(tracker.FinalizeCombat(5, CombatOutcome.Loss), "duplicate finalization");
		Equal(CombatOutcome.Win, tracker.Rows[0].Outcome, "final result is stable");
		tracker.BeginCombat(Snapshot(6, 3)); tracker.FinalizeCombat(6, CombatOutcome.Unknown);
		True(tracker.ApplyDefinitiveResult(6, CombatOutcome.Loss), "definitive match event fills a finalized unknown last combat"); Equal(CombatOutcome.Loss, tracker.Rows[1].Outcome, "last combat no longer remains unknown");
		False(tracker.ApplyDefinitiveResult(5, CombatOutcome.Loss), "definitive match event does not overwrite a known combat result");
	}

	private static void RerunUpdatesTheSameRow()
	{
		var tracker = new CombatHistoryTracker(); tracker.BeginCombat(Snapshot(3, 7));
		tracker.UpdateSimulation(3, new SimulationProbabilities(.5, .1, .4));
		tracker.UpdateSimulation(3, new SimulationProbabilities(.7, .1, .2));
		Equal(1, tracker.Rows.Count, "rerun does not add a row");
		Near(.7, tracker.Rows[0].Probabilities!.Win, "rerun replaces result");
	}

	private static void StaleDataDoesNotLeak()
	{
		var tracker = new CombatHistoryTracker(); tracker.BeginCombat(Snapshot(1, 2));
		tracker.UpdateSimulation(1, new SimulationProbabilities(.6, .1, .3)); tracker.FinalizeCombat(1, CombatOutcome.Win);
		tracker.BeginCombat(Snapshot(2, 3));
		True(tracker.Rows[1].Probabilities == null, "next row begins empty");
		True(tracker.UpdateSimulation(1, new SimulationProbabilities(.1, .1, .8)), "late rerun updates its finalized row"); Near(.1, tracker.Rows[0].Probabilities!.Win, "late result remains on prior turn"); True(tracker.Rows[1].Probabilities == null, "late result does not leak into next turn");
	}

	private static void ParserAcceptsCulturesAndRejectsIncompleteValues()
	{
		True(BobsBuddyPercentageParser.TryParseTriple("64.2%", "3.1%", "32.7%", CultureInfo.GetCultureInfo("en-US"), out var en), "English percentages");
		Near(.642, en!.Win, "English value");
		True(BobsBuddyPercentageParser.TryParseTriple("64,2 %", "3,1 %", "32,7 %", CultureInfo.GetCultureInfo("ru-RU"), out var ru), "comma percentages");
		Near(.031, ru!.Tie, "comma value");
		False(BobsBuddyPercentageParser.TryParseTriple("-", "3%", "97%", CultureInfo.InvariantCulture, out _), "placeholder rejected");
		False(BobsBuddyPercentageParser.TryParseTriple("≥60%", "3%", "37%", CultureInfo.InvariantCulture, out _), "inequality rejected");
		False(BobsBuddyPercentageParser.TryParseTriple("60%", "", "40%", CultureInfo.InvariantCulture, out _), "missing value rejected");
		False(BobsBuddyPercentageParser.TryParseTriple("60%", "3%", "20%", CultureInfo.InvariantCulture, out _), "bad total rejected");
	}

	private static void OutcomeResolutionIsConservative()
	{
		Equal(CombatOutcome.Loss, CombatOutcomeResolver.Resolve(new OutcomeEvidence(40, 30, 40, 40)), "friendly damage is loss");
		Equal(CombatOutcome.Win, CombatOutcomeResolver.Resolve(new OutcomeEvidence(40, 40, 40, 25)), "opponent damage is win");
		Equal(CombatOutcome.Tie, CombatOutcomeResolver.Resolve(new OutcomeEvidence(40, 40, 40, 40)), "no damage is tie");
		Equal(CombatOutcome.Unknown, CombatOutcomeResolver.Resolve(new OutcomeEvidence(40, 30, 40, 30)), "both damage is ambiguous");
		Equal(CombatOutcome.Unknown, CombatOutcomeResolver.Resolve(new OutcomeEvidence(null, null, null, null)), "missing data is unknown");
		Equal(CombatOutcome.Unknown, CombatOutcomeResolver.Resolve(new OutcomeEvidence(40, 40, 40, 30, uncertainReconnect: true)), "uncertain reconnect is unknown");
		Equal(CombatOutcome.Unknown, CombatOutcomeResolver.Resolve(new OutcomeEvidence(40, 40, 40, 40, uncertainReconnect: true)), "a late all-present snapshot does not fabricate a tie");
		Equal(CombatOutcome.Loss, CombatOutcomeResolver.Resolve(new OutcomeEvidence(null, null, null, null, friendlyDamageObserved: true, uncertainReconnect: true, friendlyDamageAmount: 9)), "observed friendly damage remains a reliable reconnect loss");
		Equal(CombatOutcome.Win, CombatOutcomeResolver.Resolve(new OutcomeEvidence(null, null, null, null, opponentDamageObserved: true, uncertainReconnect: true, opponentDamageAmount: 11)), "observed opponent damage remains a reliable reconnect win");
		Equal(CombatOutcome.Win, CombatOutcomeResolver.Resolve(new OutcomeEvidence(null, null, null, null), CombatOutcome.Win), "definitive final-match win survives missing entities");
		Equal(CombatOutcome.Loss, CombatOutcomeResolver.Resolve(new OutcomeEvidence(null, null, null, null), CombatOutcome.Loss), "definitive final-match loss survives missing entities");
		Equal<int?>(12, CombatOutcomeResolver.ResolveDamage(CombatOutcome.Win, new OutcomeEvidence(40, 40, 5, 0, opponentDamageObserved: true, opponentDamageAmount: 12)), "dealt combat damage uses the observed full hit");
		Equal<int?>(-8, CombatOutcomeResolver.ResolveDamage(CombatOutcome.Loss, new OutcomeEvidence(40, 32, 40, 40)), "received combat damage is negative");
		Equal<int?>(0, CombatOutcomeResolver.ResolveDamage(CombatOutcome.Tie, new OutcomeEvidence(40, 40, 40, 40)), "tie damage is zero");
		Equal<int?>(null, CombatOutcomeResolver.ResolveDamage(CombatOutcome.Unknown, new OutcomeEvidence(null, null, null, null)), "unknown damage stays empty");
		True(CombatOutcomeResolver.SupportsDefinitiveMatchResult(CombatOutcome.Win, new OutcomeEvidence(40, 40, 5, 0)), "opponent death correlates a final win to the combat");
		False(CombatOutcomeResolver.SupportsDefinitiveMatchResult(CombatOutcome.Loss, new OutcomeEvidence(40, 40, 40, 40)), "a shopping-phase surrender cannot rewrite the previous combat");
		False(CombatOutcomeResolver.SupportsDefinitiveMatchResult(CombatOutcome.Tie, new OutcomeEvidence(40, 40, 40, 40)), "a delayed global tie is not correlated without an active combat");
	}

	private static void OutcomeRecoveryRequiresStableEvidence()
	{
		var gate = new OutcomeRecoveryGate();
		var first = new OutcomeEvidence(40, 40, 40, 30);
		var changed = new OutcomeEvidence(40, 40, 40, 28);
		False(gate.TryConfirm(CombatOutcome.Win, first), "first outcome recovery poll only seeds a candidate");
		False(gate.TryConfirm(CombatOutcome.Win, changed), "changed recovery evidence replaces the candidate");
		True(gate.TryConfirm(CombatOutcome.Win, changed), "two identical recovery polls confirm the result");
		False(gate.TryConfirm(CombatOutcome.Unknown, changed), "unknown evidence resets recovery confirmation");
		False(gate.TryConfirm(CombatOutcome.Win, changed), "confirmation starts over after an unknown poll");
		var uncertain = new OutcomeEvidence(40, 40, 40, 28, opponentDamageObserved: true, uncertainReconnect: true, opponentDamageAmount: 12);
		var certain = new OutcomeEvidence(40, 40, 40, 28, opponentDamageObserved: true, uncertainReconnect: false, opponentDamageAmount: 12);
		False(gate.TryConfirm(CombatOutcome.Win, uncertain), "reconnect certainty is part of recovery evidence");
		False(gate.TryConfirm(CombatOutcome.Win, certain), "changing reconnect certainty restarts confirmation");
	}

	private static void InterruptedCombatKeepsObservedDamage()
	{
		var win = CombatOutcomeResolver.ResolveObservedDamage(0, 15, out var dealtDamage);
		Equal(CombatOutcome.Win, win, "an interrupted combat keeps a reliable observed win");
		Equal<int?>(15, dealtDamage, "an interrupted combat keeps dealt damage");
		var loss = CombatOutcomeResolver.ResolveObservedDamage(8, 0, out var receivedDamage);
		Equal(CombatOutcome.Loss, loss, "an interrupted combat keeps a reliable observed loss");
		Equal<int?>(-8, receivedDamage, "an interrupted combat keeps received damage");
		var ambiguous = CombatOutcomeResolver.ResolveObservedDamage(8, 15, out var ambiguousDamage);
		Equal(CombatOutcome.Unknown, ambiguous, "two observed damage sides remain ambiguous");
		Equal<int?>(null, ambiguousDamage, "ambiguous interrupted damage remains empty");
		var missing = CombatOutcomeResolver.ResolveObservedDamage(0, 0, out var missingDamage);
		Equal(CombatOutcome.Unknown, missing, "missing interrupted evidence does not fabricate a tie");
		Equal<int?>(null, missingDamage, "missing interrupted damage remains empty");
	}

	private static void CaptureGateRejectsStalePartialAndErrorStates()
	{
		var gate = new BobsBuddyCaptureGate(); gate.BeginCombat();
		False(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "old valid values are not accepted before reset");
		False(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _), "reset arms the combat");
		False(gate.TryCapture(BobsBuddyCaptureState.Unsupported, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "partial Duo state rejected");
		False(gate.TryCapture(BobsBuddyCaptureState.Combat, false, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "error state rejected");
		True(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out var captured), "complete result accepted"); Near(.6, captured!.Win, "captured result");
		False(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "duplicate notification rejected");
		True(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out _), "rerun accepted");

		var reconnect = new BobsBuddyCaptureGate(); reconnect.BeginCombat(true);
		False(reconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "55%", "10%", "35%", CultureInfo.InvariantCulture, out _, guardedCheck: false), "a complete notification without the reset is remembered but not accepted");
		False(reconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "55%", "10%", "35%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "a later guarded check cannot prove ownership without a reset");
		reconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _);
		False(reconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "55%", "10%", "35%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "recovery after reset seeds confirmation");
		True(reconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "55%", "10%", "35%", CultureInfo.InvariantCulture, out var recovered, guardedCheck: true), "guarded recovery confirms a result after the current reset"); Near(.55, recovered!.Win, "confirmed recovered result");
		var changed = new BobsBuddyCaptureGate(); changed.BeginCombat(true);
		changed.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _);
		False(changed.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "first guarded value can be stale");
		False(changed.TryCapture(BobsBuddyCaptureState.Combat, true, true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "a changed guarded value becomes a new candidate instead of being published");
		True(changed.TryCapture(BobsBuddyCaptureState.Combat, true, true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out var refreshed, guardedCheck: true), "the changed candidate requires a second identical guarded check"); Near(.70, refreshed!.Win, "stable refreshed recovery result");
		var midCombatReconnect = new BobsBuddyCaptureGate(); midCombatReconnect.BeginCombat(); midCombatReconnect.EnableGuardedRecovery();
		midCombatReconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _);
		False(midCombatReconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "65%", "5%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "mid-combat recovery starts with a guarded candidate");
		True(midCombatReconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "65%", "5%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "mid-combat recovery also succeeds after a stable second check");
		midCombatReconnect.EndCombat(); False(midCombatReconnect.TryCapture(BobsBuddyCaptureState.Combat, true, true, "75%", "5%", "20%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "values from a later combat cannot pass through a closed prior-turn gate");
		var late = new BobsBuddyCaptureGate(); late.BeginCombat(true);
		late.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _);
		False(late.TryCapture(BobsBuddyCaptureState.Shopping, true, true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "shopping results are rejected outside explicit post-combat recovery");
		False(late.TryCapture(BobsBuddyCaptureState.Shopping, true, true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "first late shopping value only seeds guarded confirmation");
		True(late.TryCapture(BobsBuddyCaptureState.Shopping, true, true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out var lateResult, guardedCheck: true, allowPostCombatState: true), "stable late shopping result is accepted for the bound turn"); Near(1, lateResult!.Win, "late 100-percent win");
		var partialLate = new BobsBuddyCaptureGate(); partialLate.BeginCombat(true); False(partialLate.TryCapture(BobsBuddyCaptureState.Unsupported, true, true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "partial Duo shopping results remain rejected");
	}

	private static void PostCombatRecoveryAcceptsStableCombatOrShoppingData()
	{
		var gate = new BobsBuddyCaptureGate(); gate.BeginCombat(true);
		False(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _), "combat placeholder arms the current turn");
		False(gate.TryCapture(BobsBuddyCaptureState.Shopping, true, true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "first post-combat value is only a candidate even after a reset");
		True(gate.TryCapture(BobsBuddyCaptureState.Shopping, true, true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "stable post-combat value is accepted");
		var oldTurn = new BobsBuddyCaptureGate(); oldTurn.BeginCombat(true);
		False(oldTurn.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _), "old turn reset observed");
		False(oldTurn.TryCapture(BobsBuddyCaptureState.Combat, true, true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "first late Combat-state value is only a candidate");
		True(oldTurn.TryCapture(BobsBuddyCaptureState.Combat, true, true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out var lateCombatResult, guardedCheck: true, allowPostCombatState: true), "stable late Combat-state value is accepted after the game combat phase ended");
		Near(.70, lateCombatResult!.Win, "late Combat-state win chance");
	}

	private static void GhostIdentityIsKeptWithoutGuessing()
	{
		var tracker = new CombatHistoryTracker(); tracker.BeginCombat(Snapshot(4, 9, true));
		True(tracker.Rows[0].Snapshot.IsGhost, "ghost marker retained");
		Equal(9, tracker.Rows[0].Snapshot.OpponentPlayerId, "HDT player identity retained");
	}

	private static void ReconnectDuplicateIsIgnored()
	{
		var tracker = new CombatHistoryTracker(); tracker.BeginCombat(Snapshot(8, 4)); tracker.FinalizeCombat(8, CombatOutcome.Unknown);
		True(tracker.BeginCombat(Snapshot(8, 4)) == null, "completed reconnect turn ignored");
		Equal(1, tracker.Rows.Count, "no reconnect duplicate");
	}

	private static void PostMatchRetentionAndNewMatchReset()
	{
		var tracker = new CombatHistoryTracker(); tracker.BeginCombat(Snapshot(1, 2)); tracker.FinalizeCombat(1, CombatOutcome.Tie); tracker.EndMatch();
		True(tracker.RetainingCompletedMatch, "completed match retained"); Equal(1, tracker.Rows.Count, "game end keeps rows");
		var changes = 0; tracker.Changed += (_, __) => changes++; False(tracker.EndMatch(), "repeated game end is idempotent"); Equal(0, changes, "repeated game end emits no redraw notification");
		var retainedSnapshot = tracker.SnapshotRows();
		tracker.StartNewMatch(); False(tracker.RetainingCompletedMatch, "retention cleared"); Equal(0, tracker.Rows.Count, "new match clears rows");
		Equal(1, retainedSnapshot.Count, "an overlay snapshot is stable when the tracker starts a new match");
	}

	private static void PostMatchStaleSoloStateDoesNotStartANewMatch()
	{
		var gate = new SoloMatchReentryGate(); True(gate.ShouldTrack(true), "initial Solo entry is tracked");
		gate.MatchEnded(); False(gate.ShouldTrack(true), "stale Solo state after game end is blocked");
		False(gate.ShouldTrack(false), "observing mode exit remains outside tracking"); True(gate.ShouldTrack(true), "a later explicit Solo re-entry starts the next match");
		gate.MatchEnded(); gate.GameStarted(); True(gate.ShouldTrack(true), "OnGameStart permits a new match even before mode metadata changes");
	}

	private static void ReconnectGameStartDoesNotClearTheExistingMatch()
	{
		var gate = new ReconnectGameStartGate(); gate.Notify();
		Equal(GameStartDecision.Wait, gate.Resolve(false, false, 20000), "game start remains pending while HDT has not restored mode metadata");
		Equal(GameStartDecision.ContinueExistingMatch, gate.Resolve(true, true, 21000), "a delayed reconnect marker preserves the existing match");
		Equal(GameStartDecision.None, gate.Resolve(true, true, 22000), "the reconnect decision is consumed once");
		gate.Notify(); Equal(GameStartDecision.Wait, gate.Resolve(true, false, 50000), "the decision window starts only after Solo metadata becomes available");
		Equal(GameStartDecision.Wait, gate.Resolve(true, false, 59999), "a possible reconnect keeps the existing match during the decision window");
		Equal(GameStartDecision.StartNewMatch, gate.Resolve(true, false, 60000), "a confirmed non-reconnect starts a new match");
	}

	private static void TransientTurnAdvanceDoesNotCreateAPhantomCombat()
	{
		var gate = new CombatTurnAdvanceGate();
		var tracker = new CombatHistoryTracker(); tracker.BeginCombat(Snapshot(8, 3)); tracker.UpdateSimulation(8, new SimulationProbabilities(1, 0, 0));
		False(gate.ShouldRollOver(8, 9, true, 1000), "a changed turn while the old combat flag is still set starts confirmation");
		False(gate.ShouldRollOver(8, 9, false, 1050), "entering shopping cancels the apparent skipped combat transition");
		var evidence = new OutcomeEvidence(null, null, null, null, opponentDamageObserved: true, opponentDamageAmount: 15);
		var outcome = CombatOutcomeResolver.Resolve(evidence); tracker.FinalizeCombat(8, outcome, CombatOutcomeResolver.ResolveDamage(outcome, evidence));
		Equal(1, tracker.Rows.Count, "the shopping transition does not create a phantom ninth row");
		Equal(CombatOutcome.Win, tracker.Rows[0].Outcome, "the eighth combat keeps its observed win"); Equal<int?>(15, tracker.Rows[0].CombatDamage, "the eighth combat keeps its observed damage");
		tracker.BeginCombat(Snapshot(9, 4)); Equal(2, tracker.Rows.Count, "the ninth row starts only on the real next combat"); True(tracker.Rows[1].Probabilities == null, "the ninth row does not inherit the eighth simulation");
	}

	private static void StableTurnAdvanceRecoversASkippedPhaseEdge()
	{
		var gate = new CombatTurnAdvanceGate();
		False(gate.ShouldRollOver(2, 7, true, 1000), "a reconnect into a later combat first becomes a candidate");
		False(gate.ShouldRollOver(2, 7, true, 2999), "the later combat must remain stable for the full confirmation interval");
		True(gate.ShouldRollOver(2, 7, true, 3000), "a stable later combat recovers the skipped phase edge");
		False(gate.ShouldRollOver(7, 7, true, 4000), "the same combat is not treated as a new turn");
		False(gate.ShouldRollOver(7, 0, true, 5000), "temporarily missing turn metadata does not discard the active combat");
	}

	private static void LateSimulationBindingStopsAtTheNextCombat()
	{
		Equal(LateSimulationRecoveryAction.Poll, LateSimulationRecoveryPolicy.Decide(true, false, false, false), "shopping continues late simulation recovery");
		Equal(LateSimulationRecoveryAction.Stop, LateSimulationRecoveryPolicy.Decide(true, false, false, true), "the next observed combat edge closes the prior turn binding");
		Equal(LateSimulationRecoveryAction.None, LateSimulationRecoveryPolicy.Decide(true, true, false, true), "an unresolved game start cannot poll the prior turn binding");
		Equal(LateSimulationRecoveryAction.Poll, LateSimulationRecoveryPolicy.Decide(true, false, null, true), "a stale post-match combat flag does not discard GameOver recovery");
		Equal(LateSimulationRecoveryAction.None, LateSimulationRecoveryPolicy.Decide(false, false, false, false), "a closed binding performs no recovery work");
	}

	private static void FormattingSupportsBothLayouts()
	{
		var p = new SimulationProbabilities(.642, .031, .327);
		Equal("64.2", HistoryFormatting.NormalProbability(p.Win, CultureInfo.InvariantCulture), "normal formatting");
		Equal("64/3/33", HistoryFormatting.CompactProbabilities(p, CultureInfo.InvariantCulture), "compact formatting");
		Equal("3/3/95", HistoryFormatting.CompactProbabilities(new SimulationProbabilities(.025, .025, .95), CultureInfo.InvariantCulture), "compact midpoint percentages round away from zero");
		False(new SimulationProbabilities(.6, .1, .3).GetHashCode() == new SimulationProbabilities(.3, .1, .6).GetHashCode(), "probability hash preserves outcome order");
		Equal(string.Empty, HistoryFormatting.CompactProbabilities(null, CultureInfo.InvariantCulture), "missing values remain empty");
		Equal(string.Empty, HistoryFormatting.AnomalyMarker(AnomalySeverity.None), "ordinary outcome has no marker"); Equal("!", HistoryFormatting.AnomalyMarker(AnomalySeverity.Unusual), "visible unusual marker"); Equal("!!", HistoryFormatting.AnomalyMarker(AnomalySeverity.VeryUnusual), "visible very unusual marker"); Equal("!!!", HistoryFormatting.AnomalyMarker(AnomalySeverity.Extreme), "visible extreme marker");
		Equal("12", HistoryFormatting.CombatDamage(12, CultureInfo.InvariantCulture), "dealt damage uses an unsigned number"); Equal("8", HistoryFormatting.CombatDamage(-8, CultureInfo.InvariantCulture), "received damage uses an unsigned number"); Equal("0", HistoryFormatting.CombatDamage(0, CultureInfo.InvariantCulture), "tie damage is zero"); Equal(string.Empty, HistoryFormatting.CombatDamage(null, CultureInfo.InvariantCulture), "missing damage remains empty");
	}

	private static void LongHistoryIsRetainedForViewporting()
	{
		var tracker = new CombatHistoryTracker();
		for(var turn = 1; turn <= 21; turn++) { tracker.BeginCombat(Snapshot(turn, turn + 1)); tracker.FinalizeCombat(turn, CombatOutcome.Tie); }
		Equal(21, tracker.Rows.Count, "history does not discard older rows");
		Equal(7, Math.Max(0, tracker.Rows.Count - 14), "default hidden-row count");
	}

	private static void ViewportChangesKeepTheNewestRowsVisible()
	{
		True(HistoryViewportPolicy.ShouldScrollToNewest(20, 20, 14, 6, true), "reducing visible rows while at the end keeps the newest combat visible");
		False(HistoryViewportPolicy.ShouldScrollToNewest(20, 20, 14, 6, false), "changing the viewport does not discard an intentional older scroll position");
		True(HistoryViewportPolicy.ShouldScrollToNewest(20, 21, 6, 6, false), "a newly added combat remains automatically visible");
	}

	private static void AnomaliesAndSummaryAreSymmetric()
	{
		var tracker = new CombatHistoryTracker(); tracker.SetThresholds(new AnomalyThresholds(.50, .80, .95));
		tracker.BeginCombat(Snapshot(1, 2)); tracker.UpdateSimulation(1, new SimulationProbabilities(.005, .025, .97)); tracker.FinalizeCombat(1, CombatOutcome.Win);
		tracker.BeginCombat(Snapshot(2, 3)); tracker.UpdateSimulation(2, new SimulationProbabilities(.8, .15, .05)); tracker.FinalizeCombat(2, CombatOutcome.Loss);
		Equal(AnomalySeverity.Extreme, tracker.Rows[0].Anomaly, "missing a 97-percent expected loss is extreme"); Equal(AnomalySeverity.VeryUnusual, tracker.Rows[1].Anomaly, "missing an 80-percent expected win is very unusual");
		Equal(AnomalySeverity.Unusual, AnomalyClassifier.Classify(CombatOutcome.Tie, new SimulationProbabilities(.74, .04, .22), new AnomalyThresholds()), "missing a 74-percent expected win is marked");
		Equal(AnomalySeverity.Unusual, AnomalyClassifier.Classify(CombatOutcome.Loss, new SimulationProbabilities(.55, .10, .35), new AnomalyThresholds()), "missing a 55-percent expected win is a light anomaly");
		Equal(AnomalySeverity.None, AnomalyClassifier.Classify(CombatOutcome.Win, new SimulationProbabilities(.55, .10, .35), new AnomalyThresholds()), "the expected result is not anomalous");
		Equal(AnomalySeverity.None, AnomalyClassifier.Classify(CombatOutcome.Tie, new SimulationProbabilities(.49, .03, .48), new AnomalyThresholds()), "without a majority expected result there is no anomaly");
		Equal(AnomalySeverity.Unusual, AnomalyClassifier.Classify(CombatOutcome.Loss, new SimulationProbabilities(.34, .33, .33), new AnomalyThresholds(), strict: true), "strict mode marks a lower-chance result even without a majority");
		Equal(AnomalySeverity.None, AnomalyClassifier.Classify(CombatOutcome.Win, new SimulationProbabilities(.34, .33, .33), new AnomalyThresholds(), strict: true), "strict mode does not mark the unique most likely result");
		Equal(AnomalySeverity.None, AnomalyClassifier.Classify(CombatOutcome.Loss, new SimulationProbabilities(.34, .34, .32), new AnomalyThresholds(), strict: true), "strict mode requires one unique maximum");
		Equal(AnomalySeverity.VeryUnusual, AnomalyClassifier.Classify(CombatOutcome.Loss, new SimulationProbabilities(.80, .10, .10), new AnomalyThresholds(), strict: true), "strict mode keeps a fixed strong tier");
		Equal(AnomalySeverity.Extreme, AnomalyClassifier.Classify(CombatOutcome.Tie, new SimulationProbabilities(.95, .01, .04), new AnomalyThresholds(), strict: true), "strict mode keeps a fixed extreme tier");
		var summary = AnomalyClassifier.Summarize(tracker.Rows);
		Equal(2, summary.SampleSize, "summary reports its comparable sample size"); Equal(1, summary.ActualWins, "actual wins"); Equal(1, summary.ActualLosses, "actual losses"); Near(.805, summary.ExpectedWins, "expected wins"); Near(1.02, summary.ExpectedLosses, "expected losses");
		tracker.BeginCombat(Snapshot(3, 4)); tracker.FinalizeCombat(3, CombatOutcome.Win);
		tracker.BeginCombat(Snapshot(4, 5)); tracker.UpdateSimulation(4, new SimulationProbabilities(.4, .2, .4)); tracker.FinalizeCombat(4, CombatOutcome.Unknown);
		tracker.BeginCombat(Snapshot(5, 6)); tracker.UpdateSimulation(5, new SimulationProbabilities(.4, .2, .4));
		var comparable = AnomalyClassifier.Summarize(tracker.Rows); Equal(2, comparable.SampleSize, "summary excludes rows that are not comparable on both sides"); Equal(1, comparable.ActualWins, "actual summary uses the same eligible rows as expected");
	}

	private static void OverlayPositionRemainsStableWhenWidthChanges()
	{
		Near(25, OverlayPositionPolicy.HorizontalOffsetFromLeft(OverlaySide.Left, 1920, 300, 25), "left placement stores the left coordinate");
		Near(100, OverlayPositionPolicy.HorizontalOffsetFromLeft(OverlaySide.Right, 1920, 300, 1520), "right placement derives its offset from the preserved left coordinate");
		Near(200, OverlayPositionPolicy.HorizontalOffsetFromLeft(OverlaySide.Right, 1920, 200, 1520), "changing overlay width keeps the same left coordinate by changing the right offset");
		True(OverlayPositionPolicy.PreserveExpandedWidthWhenCollapsed(HistoryLayout.Normal), "normal collapsed overlay keeps its expanded table width");
		False(OverlayPositionPolicy.PreserveExpandedWidthWhenCollapsed(HistoryLayout.Compact), "compact collapsed overlay shrinks to its header width");
		var normal = OverlayLayoutMetrics.For(HistoryLayout.Normal); var compact = OverlayLayoutMetrics.For(HistoryLayout.Compact);
		True(compact.TitleFontSize < normal.TitleFontSize, "compact title text is smaller");
		True(compact.RowFontSize < normal.RowFontSize, "compact row text is smaller");
		True(compact.RowHeight < normal.RowHeight, "compact rows are shorter");
		True(compact.CollapseWidth < normal.CollapseWidth, "compact collapse button is narrower");
		True(compact.CollapseHeight < normal.CollapseHeight, "compact header strip and collapse button are shorter");
		Near(176, compact.CompactExpandedWidth(showHero: true, showDamage: true), "compact expanded width is calculated from every visible column");
		Near(148, compact.CompactExpandedWidth(showHero: true, showDamage: false), "compact expanded width excludes the hidden damage column");
		Near(20, compact.CompactCollapsedWidth, "compact collapsed width is calculated without relying on delayed WPF measurement");
		Near(256, OverlayPositionPolicy.CompactLeftAfterToggle(100, 176, 20, 1, collapsing: true), "compact collapse keeps the button at its expanded right edge");
		Near(100, OverlayPositionPolicy.CompactLeftAfterToggle(256, 176, 20, 1, collapsing: false), "compact expansion restores the original table position");
		Near(412, OverlayPositionPolicy.CompactLeftAfterToggle(100, 176, 20, 2, collapsing: true), "compact collapse movement accounts for overlay scale");
		Near(344, OverlayPositionPolicy.CompactDragHorizontalOffset(OverlaySide.Left, 1920, 176, 0, 20, 1, 500), "dragging a left-placed collapsed compact overlay stores the expanded table coordinate");
		Near(500, OverlayPositionPolicy.CompactCollapsedLeftFromHorizontalOffset(OverlaySide.Left, 1920, 176, 0, 20, 1, 344), "the saved left offset keeps the collapsed button at its dragged coordinate");
		Near(412, OverlayPositionPolicy.CompactCollapsedLeftFromHorizontalOffset(OverlaySide.Left, 1920, 176, 0, 20, 2, 100), "a scale change recalculates the collapsed button from the expanded anchor");
		var compactRightOffset = OverlayPositionPolicy.CompactDragHorizontalOffset(OverlaySide.Right, 1920, 176, 0, 20, 1, 500);
		Near(1400, compactRightOffset, "dragging a right-placed collapsed compact overlay stores its right offset");
		Near(500, OverlayPositionPolicy.CompactCollapsedLeftFromHorizontalOffset(OverlaySide.Right, 1920, 176, 0, 20, 1, compactRightOffset), "the saved right offset keeps the collapsed button at its dragged coordinate");
		Near(344, 1920 - 176 - compactRightOffset, "the saved right offset restores the expanded compact table coordinate");
		var compactRightOffsetWithScrollbar = OverlayPositionPolicy.CompactDragHorizontalOffset(OverlaySide.Right, 1920, 176, 10, 20, 1, 500);
		Near(1390, compactRightOffsetWithScrollbar, "the saved right offset accounts for an expanded scrollbar without moving the collapse button");
		Near(500, OverlayPositionPolicy.CompactCollapsedLeftFromHorizontalOffset(OverlaySide.Right, 1920, 176, 10, 20, 1, compactRightOffsetWithScrollbar), "the scrollbar-aware right offset keeps the collapsed button at its dragged coordinate");
		Near(344, 1920 - 186 - compactRightOffsetWithScrollbar, "the scrollbar-aware right offset restores the expanded compact table coordinate");
		True(compact.TurnWidth + compact.HeroWidth + compact.ProbabilityWidth + compact.DamageWidth + compact.ResultWidth < normal.TurnWidth + normal.HeroWidth + normal.ProbabilityWidth * 3 + normal.DamageWidth + normal.ResultWidth, "compact table is narrower");
	}

	private static void TrackerAndSettingsSnapshotsAreThreadSafe()
	{
		var tracker = new CombatHistoryTracker();
		Exception? trackerFailure = null;
		var writer = Task.Run(() =>
		{
			try { for(var turn = 1; turn <= 300; turn++) tracker.BeginCombat(Snapshot(turn, turn + 1)); }
			catch(Exception ex) { trackerFailure = ex; }
		});
		var reader = Task.Run(() =>
		{
			try { for(var index = 0; index < 1000; index++) tracker.SnapshotRows(); }
			catch(Exception ex) { trackerFailure = ex; }
		});
		Task.WaitAll(writer, reader);
		True(trackerFailure == null, "history snapshots remain safe while combats are added");

		var settings = new PluginSettings(); settings.SetPosition(0, 0);
		Exception? settingsFailure = null;
		var positionWriter = Task.Run(() =>
		{
			try { for(var index = 0; index < 1000; index++) settings.SetPosition(index, -index); }
			catch(Exception ex) { settingsFailure = ex; }
		});
		var positionReader = Task.Run(() =>
		{
			try
			{
				for(var index = 0; index < 1000; index++)
				{
					var snapshot = settings.Snapshot();
					if(snapshot.VerticalOffset != -snapshot.HorizontalOffset) throw new InvalidOperationException("torn position snapshot");
				}
			}
			catch(Exception ex) { settingsFailure = ex; }
		});
		Task.WaitAll(positionWriter, positionReader);
		True(settingsFailure == null, "position snapshots are atomic while the overlay is dragged");
	}

	private static void VersionAndMovementDefaultsAreStable()
	{
		Equal("1.4.2", PluginVersion.Display, "short displayed version"); Equal("1.4.2", PluginVersion.LocalRelease, "stable release label"); Equal("1.4.2", PluginVersion.Hdt.ToString(), "HDT version has no trailing zeroes");
		Equal(PluginVersion.LocalRelease, typeof(PluginVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, "assembly informational version matches local release label");
		var settings = new PluginSettings(); True(settings.LockOverlayPosition, "overlay movement is locked by default"); True(settings.ShowAnomalyStatus, "anomaly status is shown by default");
		False(settings.ShowDamageColumn, "combat damage column is hidden by default"); True(settings.ShowHeroColumn, "hero column is shown by default");
		True(settings.StrictAnomalies, "strict anomaly mode is enabled by default");
		Near(14, settings.HorizontalOffset, "default horizontal offset matches the release layout"); Near(155, settings.VerticalOffset, "default vertical offset matches the release layout");
		settings.HorizontalOffset = 4000; settings.VerticalOffset = -3000; settings.Normalize(); Near(4000, settings.HorizontalOffset, "dragging far across a wide or multi-monitor desktop keeps the horizontal position"); Near(-3000, settings.VerticalOffset, "dragging far across a tall or multi-monitor desktop keeps the vertical position");
		Near(1, settings.Opacity, "overlay is fully opaque by default"); Near(1, settings.BackgroundOpacity, "background is fully opaque by default"); Equal(OverlayBackgroundMode.Full, settings.BackgroundMode, "full background is the default");
		False(settings.ShowMatchSummary, "expected-versus-actual summary is hidden by default"); False(settings.ShowOverlayPreview, "overlay preview is hidden by default");
		using(var reader = new StringReader("<PluginSettings><ShowCombatDamageColumn>true</ShowCombatDamageColumn></PluginSettings>"))
		{
			var migrated = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(reader); False(migrated.ShowDamageColumn, "the old default-on damage setting does not carry into the new default-off option");
		}
		Near(51, settings.UnusualExpectedPercent, "light anomaly expected-result threshold"); Near(80, settings.VeryUnusualExpectedPercent, "strong anomaly expected-result threshold"); Near(95, settings.ExtremeExpectedPercent, "extreme anomaly expected-result threshold");
		False(settings.HideWhenHearthstoneNotForeground, "focus hiding is disabled by default");
		var copy = new PluginSettings { LockOverlayPosition = false, HideWhenHearthstoneNotForeground = false, ShowOverlayPreview = true, BackgroundOpacity = .35, BackgroundMode = OverlayBackgroundMode.Header }; settings.CopyFrom(copy); False(settings.LockOverlayPosition, "movement lock is copied with visual settings"); False(settings.HideWhenHearthstoneNotForeground, "focus behavior is copied with visual settings"); True(settings.ShowOverlayPreview, "preview behavior is copied with settings"); Near(.35, settings.BackgroundOpacity, "background opacity is copied"); Equal(OverlayBackgroundMode.Header, settings.BackgroundMode, "background mode is copied");
		var bounds = new PluginSettings { HorizontalOffset = 20000, UnusualExpectedPercent = 50, VeryUnusualExpectedPercent = 100 }; bounds.Normalize(); Near(PluginSettings.MaximumPositionOffset, bounds.HorizontalOffset, "finite visual values saturate at the nearest boundary"); Near(51, bounds.UnusualExpectedPercent, "the light anomaly threshold cannot be set to an ineffective 50 percent"); Near(99, bounds.VeryUnusualExpectedPercent, "the strong anomaly threshold maximum does not jump back to its default");
		var invalid = new PluginSettings { Scale = double.NaN, BackgroundOpacity = double.PositiveInfinity }; invalid.Normalize(); Near(1, invalid.Scale, "non-finite settings use a safe fallback"); Near(1, invalid.BackgroundOpacity, "non-finite background opacity uses a safe fallback");
		Throws<ArgumentOutOfRangeException>(() => new SimulationProbabilities(double.NaN, 0, 1), "probability model rejects NaN");
		Throws<ArgumentOutOfRangeException>(() => new SimulationProbabilities(.8, .8, 0), "probability model rejects an invalid total");
	}

	private static void SettingsOpenWithoutInitializedOwnerHandle()
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
				var owner = new Window();
				application.MainWindow = owner;

				var settingsWindow = new SettingsWindow(new PluginSettings(), _ => { }, PluginVersion.Hdt);
				Equal(WindowStartupLocation.CenterScreen, settingsWindow.WindowStartupLocation, "settings use the screen when the owner handle is unavailable");
				settingsWindow.Close(); owner.Close(); application.Shutdown();
			}
			catch(Exception ex) { failure = ex; }
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start(); thread.Join();
		if(failure != null) throw new InvalidOperationException("settings should open without a game window", failure);
	}

	private static void VersionParsingAndManualUpdateCheck()
	{
		True(ReleaseVersion.TryParse("v2.8", out var shortVersion), "v-prefixed short release tag is accepted");
		True(ReleaseVersion.TryParse("2.8.1.0", out var longVersion), "four-part release tag is accepted");
		True(ReleaseVersion.TryParse("2.8.1-rc.2", out var prerelease), "SemVer prerelease is accepted");
		True(longVersion!.CompareTo(shortVersion) > 0, "missing version parts compare as zero");
		True(longVersion.CompareTo(prerelease) > 0, "stable release sorts after prerelease");
		False(ReleaseVersion.TryParse("release-2.8", out _), "unsupported tag text is rejected");
		True(VersionChecker.IsValidRepository("numbereleven-a/HDT-HistoryCombatSimulation"), "configured owner and repository are accepted");
		False(VersionChecker.IsValidRepository("api.github.com/repos/owner/repo"), "repository override cannot replace the API host");
		False(VersionChecker.IsValidRepository("owner/."), "single-dot repository names are rejected");
		False(VersionChecker.IsValidRepository("owner/.."), "repository path traversal segments are rejected");
		False(VersionChecker.IsValidRepository("owner/..."), "repository names made only of dots are rejected");

		var handler = new UpdateHandler();
		var oldRepository = Environment.GetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_REPOSITORY");
		var oldToken = Environment.GetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_TOKEN");
		try
		{
			Environment.SetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_REPOSITORY", VersionChecker.DefaultRepository);
			Environment.SetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_TOKEN", "local-test-token");
			var checker = new VersionChecker(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) });
			var result = checker.CheckAsync(new Version(1, 1, 2), CancellationToken.None).GetAwaiter().GetResult();
			True(result.UpdateAvailable, "newer GitHub release is reported");
			Equal("1.2", result.Latest.ToString(), "latest tag is displayed without v");
			Equal("api.github.com", handler.Host, "update request is restricted to GitHub API");
			Equal("Bearer", handler.AuthorizationScheme, "optional token is attached only to the request");
		}
		finally
		{
			Environment.SetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_REPOSITORY", oldRepository);
			Environment.SetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_TOKEN", oldToken);
		}
	}

	private static CombatSnapshot Snapshot(int turn, int opponent, bool ghost = false) => new CombatSnapshot(turn, opponent, opponent + 100, "TB_BaconShop_HERO_PH", ghost, 40, 40);
	private static void True(bool value, string name) { if(!value) throw new InvalidOperationException(name); }
	private static void False(bool value, string name) { if(value) throw new InvalidOperationException(name); }
	private static void Equal<T>(T expected, T actual, string name) { if(!Equals(expected, actual)) throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual); }
	private static void Same(object? expected, object? actual, string name) { if(!ReferenceEquals(expected, actual)) throw new InvalidOperationException(name); }
	private static void Near(double expected, double actual, string name) { if(Math.Abs(expected - actual) > .00001) throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual); }
	private static void Throws<T>(Action action, string name) where T : Exception { try { action(); } catch(T) { return; } throw new InvalidOperationException(name); }

	private sealed class UpdateHandler : HttpMessageHandler
	{
		public string? Host { get; private set; }
		public string? AuthorizationScheme { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Host = request.RequestUri?.Host;
			AuthorizationScheme = request.Headers.Authorization?.Scheme;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"tag_name\":\"v1.2\"}")
			});
		}
	}
}
