using System;
using System.Threading;

namespace HistoryCombatSimulation
{
	public enum CombatOutcome { Unknown, Win, Tie, Loss }
	public enum AnomalySeverity { None, Unusual, VeryUnusual, Extreme }

	public sealed class CombatSnapshot
	{
		public CombatSnapshot(int turn, int opponentPlayerId, int opponentEntityId, string heroCardId, bool isGhost, int? friendlyDurability, int? opponentDurability)
		{
			Turn = turn;
			OpponentPlayerId = opponentPlayerId;
			OpponentEntityId = opponentEntityId;
			HeroCardId = heroCardId ?? string.Empty;
			IsGhost = isGhost;
			FriendlyDurability = friendlyDurability;
			OpponentDurability = opponentDurability;
		}

		public int Turn { get; }
		public int OpponentPlayerId { get; }
		public int OpponentEntityId { get; }
		public string HeroCardId { get; }
		public bool IsGhost { get; }
		public int? FriendlyDurability { get; }
		public int? OpponentDurability { get; }
	}

	public sealed class SimulationProbabilities : IEquatable<SimulationProbabilities>
	{
		public SimulationProbabilities(double win, double tie, double loss)
		{
			if(!IsProbability(win) || !IsProbability(tie) || !IsProbability(loss) || Math.Abs(win + tie + loss - 1d) > .015)
				throw new ArgumentOutOfRangeException(nameof(win), "Probabilities must be finite values from 0 to 1 with a total close to 1.");
			Win = win;
			Tie = tie;
			Loss = loss;
		}
		public double Win { get; }
		public double Tie { get; }
		public double Loss { get; }
		public bool Equals(SimulationProbabilities? other) => other != null && Win.Equals(other.Win) && Tie.Equals(other.Tie) && Loss.Equals(other.Loss);
		public override bool Equals(object? obj) => Equals(obj as SimulationProbabilities);
		public override int GetHashCode()
		{
			unchecked
			{
				var hash = 17;
				hash = hash * 31 + Win.GetHashCode();
				hash = hash * 31 + Tie.GetHashCode();
				hash = hash * 31 + Loss.GetHashCode();
				return hash;
			}
		}
		private static bool IsProbability(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
	}

	public sealed class CombatRow
	{
		private static long _nextIdentity;
		internal CombatRow(CombatSnapshot snapshot) : this(snapshot, Interlocked.Increment(ref _nextIdentity)) { }
		internal CombatRow(CombatSnapshot snapshot, long identity) { Snapshot = snapshot; Identity = identity; }
		internal long Identity { get; }
		public CombatSnapshot Snapshot { get; }
		public SimulationProbabilities? Probabilities { get; internal set; }
		public CombatOutcome Outcome { get; internal set; }
		public bool IsFinalized { get; internal set; }
		public AnomalySeverity Anomaly { get; internal set; }
		public int? CombatDamage { get; internal set; }
	}

	public sealed class OutcomeEvidence
	{
		public OutcomeEvidence(int? friendlyBefore, int? friendlyAfter, int? opponentBefore, int? opponentAfter, bool friendlyDamageObserved = false, bool opponentDamageObserved = false, bool uncertainReconnect = false, int friendlyDamageAmount = 0, int opponentDamageAmount = 0)
		{
			FriendlyBefore = friendlyBefore; FriendlyAfter = friendlyAfter; OpponentBefore = opponentBefore; OpponentAfter = opponentAfter;
			FriendlyDamageObserved = friendlyDamageObserved; OpponentDamageObserved = opponentDamageObserved; UncertainReconnect = uncertainReconnect;
			FriendlyDamageAmount = Math.Max(0, friendlyDamageAmount); OpponentDamageAmount = Math.Max(0, opponentDamageAmount);
		}
		public int? FriendlyBefore { get; }
		public int? FriendlyAfter { get; }
		public int? OpponentBefore { get; }
		public int? OpponentAfter { get; }
		public bool FriendlyDamageObserved { get; }
		public bool OpponentDamageObserved { get; }
		public bool UncertainReconnect { get; }
		public int FriendlyDamageAmount { get; }
		public int OpponentDamageAmount { get; }
	}
}
