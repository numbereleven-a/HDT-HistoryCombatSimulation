using System;
using System.Globalization;

namespace HistoryCombatSimulation
{
	public static class BobsBuddyPercentageParser
	{
		public static bool TryParseTriple(string? win, string? tie, string? loss, CultureInfo culture, out SimulationProbabilities? result)
		{
			result = null;
			if(!TryParse(win, culture, out var w) || !TryParse(tie, culture, out var t) || !TryParse(loss, culture, out var l))
				return false;
			var total = w + t + l;
			if(Math.Abs(total - 1d) > .015)
				return false;
			result = new SimulationProbabilities(w, t, l);
			return true;
		}

		private static bool TryParse(string? text, CultureInfo culture, out double value)
		{
			value = 0;
			if(string.IsNullOrWhiteSpace(text))
				return false;
			if(text!.IndexOf('≥') >= 0 || text.Trim() == "-")
				return false;
			var percent = culture.NumberFormat.PercentSymbol;
			var normalized = text!.Trim();
			if(!string.IsNullOrEmpty(percent) && normalized.EndsWith(percent, StringComparison.CurrentCulture))
				normalized = normalized.Substring(0, normalized.Length - percent.Length).Trim();
			else if(normalized.EndsWith("%", StringComparison.Ordinal))
				normalized = normalized.Substring(0, normalized.Length - 1).Trim();
			if(!double.TryParse(normalized, NumberStyles.Number, culture, out var parsed) || parsed < 0 || parsed > 100)
				return false;
			value = parsed / 100d;
			return true;
		}
	}
}
