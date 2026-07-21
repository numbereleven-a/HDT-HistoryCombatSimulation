using System;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace HistoryCombatSimulation
{
	internal static class HdtEventBridge
	{
		private static readonly object Sync = new object();
		private static WeakReference<HistoryCombatSimulationPlugin>? _active;
		private static bool _subscribed;

		public static void Attach(HistoryCombatSimulationPlugin plugin)
		{
			lock(Sync)
			{
				_active = new WeakReference<HistoryCombatSimulationPlugin>(plugin);
				if(_subscribed) return;
				GameEvents.OnEntityWillTakeDamage.Add(HandleDamage);
				GameEvents.OnGameWon.Add(HandleWin);
				GameEvents.OnGameLost.Add(HandleLoss);
				GameEvents.OnGameTied.Add(HandleTie);
				GameEvents.OnGameStart.Add(HandleGameStart);
				GameEvents.OnGameEnd.Add(HandleGameEnd);
				_subscribed = true;
			}
		}

		public static void Detach(HistoryCombatSimulationPlugin plugin)
		{
			lock(Sync)
			{
				if(_active != null && _active.TryGetTarget(out var target) && ReferenceEquals(target, plugin)) _active = null;
			}
		}

		private static void HandleDamage(PredamageInfo info) => Dispatch(plugin => plugin.HandleDamage(info));
		private static void HandleWin() => Dispatch(plugin => plugin.HandleDefinitiveMatchResult(CombatOutcome.Win));
		private static void HandleLoss() => Dispatch(plugin => plugin.HandleDefinitiveMatchResult(CombatOutcome.Loss));
		private static void HandleTie() => Dispatch(plugin => plugin.HandleDefinitiveMatchResult(CombatOutcome.Tie));
		private static void HandleGameStart() => Dispatch(plugin => plugin.HandleGameStart());
		private static void HandleGameEnd() => Dispatch(plugin => plugin.HandleGameEnd());

		private static void Dispatch(Action<HistoryCombatSimulationPlugin> action)
		{
			HistoryCombatSimulationPlugin? plugin = null;
			lock(Sync) _active?.TryGetTarget(out plugin);
			if(plugin?.IsLoaded != true) return;
			try { action(plugin); }
			catch(Exception ex) { Log.Error("History Combat Simulation: HDT event handling failed (" + ex.GetType().Name + ")."); }
		}
	}
}
