using System;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using HistoryCombatSimulation;

internal static class CombatLifecycleTests
{
	public static void StableOldPercentagesRemainUnknown()
	{
		var gate = new BobsBuddyCaptureGate();
		gate.BeginCombat(true);
		for(var index = 0; index < 5; index++)
			if(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true))
				throw new InvalidOperationException("Stable percentages without a current combat reset were published.");
	}

	public static void UnloadClearsCombatState()
	{
		var type = typeof(PluginVersion).Assembly.GetType("HistoryCombatSimulation.HistoryCombatSimulationPlugin", true)!;
		var plugin = Activator.CreateInstance(type)!;
		var snapshot = new CombatSnapshot(5, 2, 20, "HERO_01", false, 40, 40);
		var tracker = (CombatHistoryTracker)Field(type, "_tracker").GetValue(plugin)!;
		tracker.BeginCombat(snapshot);
		Field(type, "_activeSnapshot").SetValue(plugin, snapshot);
		Field(type, "_insideSoloMatch").SetValue(plugin, true);
		Field(type, "_wasCombat").SetValue(plugin, true);
		Field(type, "_friendlyDamageAmount").SetValue(plugin, 9);
		Field(type, "_activeSnapshotHasReliableStart").SetValue(plugin, true);
		Field(type, "_lateSimulationTurn").SetValue(plugin, 5);
		Field(type, "_pendingUnknownSnapshot").SetValue(plugin, snapshot);
		Field(type, "_awaitingDefinitiveResultTurn").SetValue(plugin, 5);
		((ReconnectGameStartGate)Field(type, "_gameStartGate").GetValue(plugin)!).Notify();
		((SoloMatchReentryGate)Field(type, "_soloMatchReentryGate").GetValue(plugin)!).MatchEnded();
		foreach(var name in new[] { "_missingSimulationPoll", "_unknownOutcomePoll", "_lateSimulationPoll", "_lateSimulationWindow" })
			((Stopwatch)Field(type, name).GetValue(plugin)!).Start();
		// Prevent settings persistence in this isolated lifecycle test.
		Field(type, "_settings").SetValue(plugin, null);
		try { type.GetMethod("OnUnload")!.Invoke(plugin, null); }
		catch(TargetInvocationException ex) when(ex.InnerException is NullReferenceException) { }
		if(tracker.Rows.Count != 0 || Field(type, "_activeSnapshot").GetValue(plugin) != null
			|| (bool)Field(type, "_insideSoloMatch").GetValue(plugin)! || Field(type, "_wasCombat").GetValue(plugin) != null)
			throw new InvalidOperationException("Unloading cleared history but retained combat controller state.");
		foreach(var name in new[] { "_pendingUnknownSnapshot", "_awaitingDefinitiveResultTurn", "_lateSimulationTurn" })
			Check(Field(type, name).GetValue(plugin) == null, name + " was cleared");
		foreach(var name in new[] { "_missingSimulationPoll", "_unknownOutcomePoll", "_lateSimulationPoll", "_lateSimulationWindow" })
			Check(!((Stopwatch)Field(type, name).GetValue(plugin)!).IsRunning, name + " was stopped");
		Check(!(bool)Field(type, "_activeSnapshotHasReliableStart").GetValue(plugin)!, "reload cannot inherit a reliable combat start");
		Check((int)Field(type, "_friendlyDamageAmount").GetValue(plugin)! == 0, "observed damage was cleared");
		Check(!((ReconnectGameStartGate)Field(type, "_gameStartGate").GetValue(plugin)!).IsPending, "pending reconnect decision was cleared");
		Check(((SoloMatchReentryGate)Field(type, "_soloMatchReentryGate").GetValue(plugin)!).ShouldTrack(true), "post-match block was cleared");
		// Exercise the same reset used on load, without initializing HDT or reading user settings.
		type.GetMethod("ResetSessionState", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(plugin, null);
		Check(tracker.BeginCombat(snapshot) != null, "the same combat can be rebound after reload");
		Field(type, "_activeSnapshot").SetValue(plugin, snapshot);
		type.GetMethod("FinalizeInterruptedCombat", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(plugin, null);
		Check(tracker.Rows[0].IsFinalized && tracker.Rows[0].Outcome == CombatOutcome.Unknown, "incomplete reloaded combat finishes as unknown");
		var next = new CombatSnapshot(6, 3, 30, "HERO_02", false, 40, 40);
		tracker.BeginCombat(next); tracker.BeginCombat(next);
		Check(tracker.Rows.Count == 2, "the following combat has one row");
	}

	public static void RecoveryRequiresResetAndContinuousCompleteValues()
	{
		var gate = new BobsBuddyCaptureGate();
		for(var combat = 0; combat < 2; combat++)
		{
			gate.BeginCombat(true);
			Check(!Read(gate), "identical prior-combat percentages remain unconfirmed");
			gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "10%", "30%", CultureInfo.InvariantCulture, out _);
			Check(!Read(gate), "partial placeholders cannot prove a reset");
			gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _);
			Check(!Read(gate), "the first complete poll seeds recovery");
			gate.TryCapture(BobsBuddyCaptureState.Combat, false, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true);
			Check(!Read(gate), "an error breaks stable confirmation");
			gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "20%", CultureInfo.InvariantCulture, out _, guardedCheck: true);
			Check(!Read(gate), "a malformed intermediate value breaks confirmation");
			Check(Read(gate), "the same probabilities in a new combat are accepted after its own reset");
			Check(gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "70%", "10%", "20%", CultureInfo.InvariantCulture, out _), "confirmed combat reruns do not require missing-data polling");
			gate.EndCombat();
			gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "-", "-", "-", CultureInfo.InvariantCulture, out _);
			Check(!Read(gate), "a closed gate cannot be rearmed by a later placeholder");
		}
		gate.BeginCombat(true);
		for(var index = 0; index < 3; index++)
			Check(!gate.TryCapture(BobsBuddyCaptureState.Shopping, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true, allowPostCombatState: true), "late recovery also requires the combat reset");
	}

	public static void SimulationEventsCannotCrossBindings()
	{
		var type = typeof(PluginVersion).Assembly.GetType("HistoryCombatSimulation.HistoryCombatSimulationPlugin", true)!;
		var plugin = Activator.CreateInstance(type)!;
		var tracker = (CombatHistoryTracker)Field(type, "_tracker").GetValue(plugin)!;
		var source = (BobsBuddyResultsSource)Field(type, "_bobsBuddy").GetValue(plugin)!;
		var handler = type.GetMethod("OnSimulationResult", BindingFlags.NonPublic | BindingFlags.Instance)!;
		Field(type, "_loaded").SetValue(plugin, true);
		Field(type, "_trackingEnabled").SetValue(plugin, true);
		Field(type, "_matchEpoch").SetValue(plugin, 10L);
		tracker.BeginCombat(new CombatSnapshot(1, 2, 20, "HERO_01", false, 40, 40));
		source.BeginCombat(1, true, 10);
		var old = new SimulationResultEventArgs(1, new SimulationProbabilities(.6, .1, .3), 10, source.Generation);
		tracker.StartNewMatch();
		tracker.BeginCombat(new CombatSnapshot(1, 3, 30, "HERO_02", false, 40, 40));
		Field(type, "_matchEpoch").SetValue(plugin, 11L);
		source.BeginCombat(1, true, 11);
		handler.Invoke(plugin, new object[] { source, old });
		Check(!tracker.ActiveRowHasSimulation, "an old event cannot address the same turn in a new match");
		var current = new SimulationResultEventArgs(1, old.Probabilities, 11, source.Generation);
		source.EndCombat();
		handler.Invoke(plugin, new object[] { source, current });
		Check(!tracker.ActiveRowHasSimulation, "closing the source invalidates a prepared event");
		source.BeginCombat(1, true, 11);
		handler.Invoke(plugin, new object[] { source, current });
		Check(!tracker.ActiveRowHasSimulation, "rebinding the same turn invalidates old capture generations");
		current = new SimulationResultEventArgs(1, old.Probabilities, 11, source.Generation);
		Field(type, "_loaded").SetValue(plugin, false);
		handler.Invoke(plugin, new object[] { source, current });
		Check(!tracker.ActiveRowHasSimulation, "unloaded plugins ignore prepared events");
		Field(type, "_loaded").SetValue(plugin, true);
		handler.Invoke(plugin, new object[] { source, current });
		Check(tracker.ActiveRowHasSimulation, "the current match and capture binding accepts its event");
		source.Dispose();
	}

	public static void QueuedUiWorkCannotCrossReload()
	{
		var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
		try
		{
			var type = typeof(PluginVersion).Assembly.GetType("HistoryCombatSimulation.HistoryCombatSimulationPlugin", true)!;
			var plugin = Activator.CreateInstance(type)!;
			var invoke = type.GetMethod("InvokeUi", BindingFlags.NonPublic | BindingFlags.Instance)!;
			Field(type, "_loaded").SetValue(plugin, true);
			var ran = false;
			var thread = new Thread(() => invoke.Invoke(plugin, new object[] { (Action)(() => ran = true), false, false, false }));
			thread.Start(); thread.Join();
			Field(type, "_loadGeneration").SetValue(plugin, 2L);
			var frame = new DispatcherFrame();
			application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
			Check(!ran, "queued UI work from a prior load must be discarded");
			invoke.Invoke(plugin, new object[] { (Action)(() => ran = true), false, false, false });
			Check(ran, "current load UI work still executes");
			var source = (BobsBuddyResultsSource)Field(type, "_bobsBuddy").GetValue(plugin)!;
			var pollRan = false;
			var renderRan = false;
			thread = new Thread(() =>
			{
				invoke.Invoke(plugin, new object[] { (Action)(() => pollRan = true), false, false, true });
				invoke.Invoke(plugin, new object[] { (Action)(() => renderRan = true), false, false, false });
			});
			thread.Start(); thread.Join();
			source.BeginCombat(1, true, 0);
			frame = new DispatcherFrame();
			application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
			Check(!pollRan, "queued polls cannot read a new capture binding");
			Check(renderRan, "binding a new combat must not cancel its queued row rendering");
			source.Dispose();
		}
		finally { application.Shutdown(); }
	}

	private static bool Read(BobsBuddyCaptureGate gate) => gate.TryCapture(BobsBuddyCaptureState.Combat, true, true, "60%", "10%", "30%", CultureInfo.InvariantCulture, out _, guardedCheck: true);
	private static void Check(bool condition, string message) { if(!condition) throw new InvalidOperationException(message); }

	private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
