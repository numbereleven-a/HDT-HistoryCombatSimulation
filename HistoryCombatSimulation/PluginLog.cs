using System;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace HistoryCombatSimulation
{
	internal static class PluginLog
	{
		public static void Error(string context, Exception exception) =>
			Log.Error(Format(context, exception), "HistoryCombatSimulation", string.Empty);

		public static void Warn(string context, Exception exception) =>
			Log.Warn(Format(context, exception), "HistoryCombatSimulation", string.Empty);

		public static void Warn(string message) =>
			Log.Warn("History Combat Simulation: " + message, "HistoryCombatSimulation", string.Empty);

		private static string Format(string context, Exception exception) =>
			"History Combat Simulation: " + context + " (" + exception.GetType().Name + ").";
	}
}
