using System;
using System.Globalization;

namespace HistoryCombatSimulation
{
	public static class HistoryFormatting
	{
		public static string NormalProbability(double? value, CultureInfo culture) => value.HasValue ? (value.Value * 100).ToString("0.#", culture) : string.Empty;
		public static string CompactProbabilities(SimulationProbabilities? values, CultureInfo culture)
		{
			if(values == null) return string.Empty;
			return string.Join("/", Math.Round(values.Win * 100, MidpointRounding.AwayFromZero).ToString("0", culture), Math.Round(values.Tie * 100, MidpointRounding.AwayFromZero).ToString("0", culture), Math.Round(values.Loss * 100, MidpointRounding.AwayFromZero).ToString("0", culture));
		}
		public static string OutcomeLetter(CombatOutcome value) => value == CombatOutcome.Win ? "W" : value == CombatOutcome.Tie ? "T" : value == CombatOutcome.Loss ? "L" : "?";
		public static string AnomalyMarker(AnomalySeverity value) => value == AnomalySeverity.Extreme ? "!!!" : value == AnomalySeverity.VeryUnusual ? "!!" : value == AnomalySeverity.Unusual ? "!" : string.Empty;
		public static string CombatDamage(int? value, CultureInfo culture) => value.HasValue ? Math.Abs(value.Value).ToString(culture) : string.Empty;
	}
}
