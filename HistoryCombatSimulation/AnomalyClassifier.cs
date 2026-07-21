using System;
using System.Collections.Generic;
using System.Linq;

namespace HistoryCombatSimulation
{
	public sealed class AnomalyThresholds
	{
		public AnomalyThresholds(double unusualExpected = .50, double veryUnusualExpected = .80, double extremeExpected = .95)
		{
			UnusualExpected = unusualExpected; VeryUnusualExpected = veryUnusualExpected; ExtremeExpected = extremeExpected;
		}
		public double UnusualExpected { get; }
		public double VeryUnusualExpected { get; }
		public double ExtremeExpected { get; }
	}

	public static class AnomalyClassifier
	{
		public static AnomalySeverity Classify(CombatOutcome outcome, SimulationProbabilities? probabilities, AnomalyThresholds thresholds, bool strict = false)
		{
			if(probabilities == null || outcome == CombatOutcome.Unknown)
				return AnomalySeverity.None;
			var expectedProbability = Math.Max(probabilities.Win, Math.Max(probabilities.Tie, probabilities.Loss));
			var maximumCount = (Same(probabilities.Win, expectedProbability) ? 1 : 0) + (Same(probabilities.Tie, expectedProbability) ? 1 : 0) + (Same(probabilities.Loss, expectedProbability) ? 1 : 0);
			if(maximumCount != 1 || !strict && expectedProbability <= .5)
				return AnomalySeverity.None;
			var expectedOutcome = expectedProbability == probabilities.Win ? CombatOutcome.Win : expectedProbability == probabilities.Tie ? CombatOutcome.Tie : CombatOutcome.Loss;
			if(outcome == expectedOutcome)
				return AnomalySeverity.None;
			if(strict)
			{
				if(expectedProbability >= .95) return AnomalySeverity.Extreme;
				if(expectedProbability >= .80) return AnomalySeverity.VeryUnusual;
				return AnomalySeverity.Unusual;
			}
			if(expectedProbability >= thresholds.ExtremeExpected) return AnomalySeverity.Extreme;
			if(expectedProbability >= thresholds.VeryUnusualExpected) return AnomalySeverity.VeryUnusual;
			if(expectedProbability >= thresholds.UnusualExpected) return AnomalySeverity.Unusual;
			return AnomalySeverity.None;
		}

		private static bool Same(double left, double right) => Math.Abs(left - right) < .000000001;

		public static MatchSummary Summarize(IEnumerable<CombatRow> rows)
		{
			var materialized = rows.Where(x => x.IsFinalized && x.Outcome != CombatOutcome.Unknown && x.Probabilities != null).ToArray();
			return new MatchSummary(
				materialized.Length,
				materialized.Count(x => x.Outcome == CombatOutcome.Win),
				materialized.Count(x => x.Outcome == CombatOutcome.Tie),
				materialized.Count(x => x.Outcome == CombatOutcome.Loss),
				materialized.Where(x => x.Probabilities != null).Sum(x => x.Probabilities!.Win),
				materialized.Where(x => x.Probabilities != null).Sum(x => x.Probabilities!.Tie),
				materialized.Where(x => x.Probabilities != null).Sum(x => x.Probabilities!.Loss));
		}
	}

	public sealed class MatchSummary
	{
		public MatchSummary(int sampleSize, int actualWins, int actualTies, int actualLosses, double expectedWins, double expectedTies, double expectedLosses)
		{ SampleSize = sampleSize; ActualWins = actualWins; ActualTies = actualTies; ActualLosses = actualLosses; ExpectedWins = expectedWins; ExpectedTies = expectedTies; ExpectedLosses = expectedLosses; }
		public int SampleSize { get; }
		public int ActualWins { get; }
		public int ActualTies { get; }
		public int ActualLosses { get; }
		public double ExpectedWins { get; }
		public double ExpectedTies { get; }
		public double ExpectedLosses { get; }
	}
}
