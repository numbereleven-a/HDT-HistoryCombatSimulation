using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using HistoryCombatSimulation;

internal static class Program
{
	private static int Main()
	{
		OneRowPerCombatAndDuplicateNotifications();
		RerunUpdatesTheSameRow();
		StaleDataDoesNotLeak();
		ParserAcceptsCulturesAndRejectsIncompleteValues();
		CaptureGateRejectsStalePartialAndErrorStates();
		OutcomeResolutionIsConservative();
		OutcomeRecoveryRequiresStableEvidence();
		GhostIdentityIsKeptWithoutGuessing();
		ReconnectDuplicateIsIgnored();
		PostMatchRetentionAndNewMatchReset();
		PostMatchStaleSoloStateDoesNotStartANewMatch();
		ReconnectGameStartDoesNotClearTheExistingMatch();
		FormattingSupportsBothLayouts();
		LongHistoryIsRetainedForViewporting();
		AnomaliesAndSummaryAreSymmetric();
		VersionAndMovementDefaultsAreStable();
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
	}

	private static void CaptureGateRejectsStalePartialAndErrorStates()
	{
		var gate = new BobsBuddyCaptureGate(); gate.BeginCombat();
		False(gate.TryCapture("Combat", "None", true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "old valid values are not accepted before reset");
		False(gate.TryCapture("Combat", "None", true, "-", "-", "-", CultureInfo.InvariantCulture, out _), "reset arms the combat");
		False(gate.TryCapture("CombatPartial", "None", true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "partial Duo state rejected");
		False(gate.TryCapture("Combat", "NotEnoughData", true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "error state rejected");
		True(gate.TryCapture("Combat", "None", true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out var captured), "complete result accepted"); Near(.6, captured!.Win, "captured result");
		False(gate.TryCapture("Combat", "None", true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _), "duplicate notification rejected");
		True(gate.TryCapture("Combat", "None", true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out _), "rerun accepted");

		var reconnect = new BobsBuddyCaptureGate(); reconnect.BeginCombat(true);
		False(reconnect.TryCapture("Combat", "None", true, "55%", "10%", "35%", CultureInfo.InvariantCulture, out _, guardedCheck: false), "a complete notification without the reset is remembered but not accepted");
		True(reconnect.TryCapture("Combat", "None", true, "55%", "10%", "35%", CultureInfo.InvariantCulture, out var recovered, guardedCheck: true), "a later guarded check confirms the remembered result"); Near(.55, recovered!.Win, "recovered result after a missed reset notification");
		var changed = new BobsBuddyCaptureGate(); changed.BeginCombat(true);
		False(changed.TryCapture("Combat", "None", true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "first guarded value can be stale");
		False(changed.TryCapture("Combat", "None", true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "a changed guarded value becomes a new candidate instead of being published");
		True(changed.TryCapture("Combat", "None", true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out var refreshed, guardedCheck: true), "the changed candidate requires a second identical guarded check"); Near(.70, refreshed!.Win, "stable refreshed recovery result");
		var midCombatReconnect = new BobsBuddyCaptureGate(); midCombatReconnect.BeginCombat(); midCombatReconnect.EnableGuardedRecovery();
		False(midCombatReconnect.TryCapture("Combat", "None", true, "65%", "5%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "mid-combat recovery starts with a guarded candidate");
		True(midCombatReconnect.TryCapture("Combat", "None", true, "65%", "5%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "mid-combat recovery also succeeds after a stable second check");
		midCombatReconnect.EndCombat(); False(midCombatReconnect.TryCapture("Combat", "None", true, "75%", "5%", "20%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "values from a later combat cannot pass through a closed prior-turn gate");
		var late = new BobsBuddyCaptureGate(); late.BeginCombat(true);
		False(late.TryCapture("Shopping", "None", true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true), "shopping results are rejected outside explicit post-combat recovery");
		False(late.TryCapture("Shopping", "None", true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "first late shopping value only seeds guarded confirmation");
		True(late.TryCapture("Shopping", "None", true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out var lateResult, guardedCheck: true, allowPostCombatState: true), "stable late shopping result is accepted for the bound turn"); Near(1, lateResult!.Win, "late 100-percent win");
		var partialLate = new BobsBuddyCaptureGate(); partialLate.BeginCombat(true); False(partialLate.TryCapture("ShoppingAfterPartial", "None", true, "100%", "0%", "0%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "partial Duo shopping results remain rejected");
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
		var gate = new ReconnectGameStartGate(); gate.Notify(1000);
		Equal(GameStartDecision.Wait, gate.Resolve(false, false, 20000), "game start remains pending while HDT has not restored mode metadata");
		Equal(GameStartDecision.ContinueExistingMatch, gate.Resolve(true, true, 21000), "a delayed reconnect marker preserves the existing match");
		Equal(GameStartDecision.None, gate.Resolve(true, true, 22000), "the reconnect decision is consumed once");
		gate.Notify(30000); Equal(GameStartDecision.Wait, gate.Resolve(true, false, 50000), "the decision window starts only after Solo metadata becomes available");
		Equal(GameStartDecision.Wait, gate.Resolve(true, false, 59999), "a possible reconnect keeps the existing match during the decision window");
		Equal(GameStartDecision.StartNewMatch, gate.Resolve(true, false, 60000), "a confirmed non-reconnect starts a new match");
	}

	private static void FormattingSupportsBothLayouts()
	{
		var p = new SimulationProbabilities(.642, .031, .327);
		Equal("64.2", HistoryFormatting.NormalProbability(p.Win, CultureInfo.InvariantCulture), "normal formatting");
		Equal("64/3/33", HistoryFormatting.CompactProbabilities(p, CultureInfo.InvariantCulture), "compact formatting");
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

	private static void VersionAndMovementDefaultsAreStable()
	{
		Equal("1.0", PluginVersion.Display, "short displayed version"); Equal("1.0", PluginVersion.LocalRelease, "stable release label"); Equal("1.0", PluginVersion.Hdt.ToString(), "HDT version has no trailing zeroes");
		Equal("1.0", typeof(PluginVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, "assembly informational version has no source revision suffix");
		var settings = new PluginSettings(); True(settings.LockOverlayPosition, "overlay movement is locked by default"); True(settings.ShowAnomalyStatus, "anomaly status is shown by default");
		False(settings.ShowDamageColumn, "combat damage column is hidden by default"); True(settings.ShowHeroColumn, "hero column is shown by default");
		True(settings.StrictAnomalies, "strict anomaly mode is enabled by default");
		Near(14, settings.HorizontalOffset, "default horizontal offset matches the release layout"); Near(155, settings.VerticalOffset, "default vertical offset matches the release layout");
		Near(1, settings.Opacity, "overlay is fully opaque by default"); Near(1, settings.BackgroundOpacity, "background is fully opaque by default");
		False(settings.ShowMatchSummary, "expected-versus-actual summary is hidden by default");
		using(var reader = new StringReader("<PluginSettings><ShowCombatDamageColumn>true</ShowCombatDamageColumn></PluginSettings>"))
		{
			var migrated = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(reader); False(migrated.ShowDamageColumn, "the old default-on damage setting does not carry into the new default-off option");
		}
		Near(51, settings.UnusualExpectedPercent, "light anomaly expected-result threshold"); Near(80, settings.VeryUnusualExpectedPercent, "strong anomaly expected-result threshold"); Near(95, settings.ExtremeExpectedPercent, "extreme anomaly expected-result threshold");
		False(settings.HideWhenHearthstoneNotForeground, "focus hiding is disabled by default");
		var copy = new PluginSettings { LockOverlayPosition = false, HideWhenHearthstoneNotForeground = false, BackgroundOpacity = .35 }; settings.CopyFrom(copy); False(settings.LockOverlayPosition, "movement lock is copied with visual settings"); False(settings.HideWhenHearthstoneNotForeground, "focus behavior is copied with visual settings"); Near(.35, settings.BackgroundOpacity, "background opacity is copied");
		var bounds = new PluginSettings { HorizontalOffset = 900, VeryUnusualExpectedPercent = 100 }; bounds.Normalize(); Near(500, bounds.HorizontalOffset, "finite visual values saturate at the nearest boundary"); Near(99, bounds.VeryUnusualExpectedPercent, "the strong anomaly threshold maximum does not jump back to its default");
		var invalid = new PluginSettings { Scale = double.NaN, BackgroundOpacity = double.PositiveInfinity }; invalid.Normalize(); Near(1, invalid.Scale, "non-finite settings use a safe fallback"); Near(1, invalid.BackgroundOpacity, "non-finite background opacity uses a safe fallback");
		Throws<ArgumentOutOfRangeException>(() => new SimulationProbabilities(double.NaN, 0, 1), "probability model rejects NaN");
		Throws<ArgumentOutOfRangeException>(() => new SimulationProbabilities(.8, .8, 0), "probability model rejects an invalid total");
	}

	private static CombatSnapshot Snapshot(int turn, int opponent, bool ghost = false) => new CombatSnapshot(turn, opponent, opponent + 100, "TB_BaconShop_HERO_PH", ghost, 40, 40);
	private static void True(bool value, string name) { if(!value) throw new InvalidOperationException(name); }
	private static void False(bool value, string name) { if(value) throw new InvalidOperationException(name); }
	private static void Equal<T>(T expected, T actual, string name) { if(!Equals(expected, actual)) throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual); }
	private static void Same(object? expected, object? actual, string name) { if(!ReferenceEquals(expected, actual)) throw new InvalidOperationException(name); }
	private static void Near(double expected, double actual, string name) { if(Math.Abs(expected - actual) > .00001) throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual); }
	private static void Throws<T>(Action action, string name) where T : Exception { try { action(); } catch(T) { return; } throw new InvalidOperationException(name); }
}
