using System;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;

namespace HistoryCombatSimulation
{
	public sealed class BattlegroundsGameAdapter
	{
		public bool IsSoloBattlegrounds => Core.Game != null && !Core.Game.IsInMenu && Core.Game.IsBattlegroundsSoloMatch && !Core.Game.IsBattlegroundsDuosMatch;
		public bool IsCombatPhase => Core.Game?.IsBattlegroundsCombatPhase == true;
		public int Turn => Core.Game?.GetTurnNumber() ?? 0;
		public bool IsReconnect => Core.Game?.CurrentGameStats?.IsReconnect == true;

		public CombatSnapshot? SnapshotCombat()
		{
			var game = Core.Game; if(game == null) return null;
			var opponentId = game.PlayerEntity?.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID) ?? 0;
			if(opponentId <= 0)
				opponentId = game.OpponentEntity?.GetTag(GameTag.PLAYER_ID) ?? 0;
			var opponent = FindHero(opponentId); var friendlyId = GetLocalPlayerId(); var friendly = FindHero(friendlyId);
			if(opponent == null || opponentId <= 0) return null;
			return new CombatSnapshot(Turn, opponentId, opponent.Id, opponent.CardId ?? string.Empty, opponent.Health <= 0,
				Durability(friendly), Durability(opponent));
		}

		public OutcomeEvidence GetOutcomeEvidence(CombatSnapshot before, int friendlyDamageAmount, int opponentDamageAmount, bool forceUncertain = false, bool allowReconnectRecovery = false)
		{
			var friendly = FindHero(GetLocalPlayerId()); var opponent = FindHero(before.OpponentPlayerId);
			var friendlyAfter = Durability(friendly); var opponentAfter = Durability(opponent);
			var uncertain = forceUncertain || IsReconnect && !allowReconnectRecovery || !before.FriendlyDurability.HasValue || !before.OpponentDurability.HasValue || !friendlyAfter.HasValue || !opponentAfter.HasValue;
			return new OutcomeEvidence(before.FriendlyDurability, friendlyAfter, before.OpponentDurability, opponentAfter, friendlyDamageAmount > 0, opponentDamageAmount > 0, uncertain, friendlyDamageAmount, opponentDamageAmount);
		}

		public DamageTarget IdentifyDamageTarget(Entity entity, CombatSnapshot active)
		{
			if(entity == null || !entity.IsHero) return DamageTarget.None;
			var playerId = entity.GetTag(GameTag.PLAYER_ID);
			if(playerId == GetLocalPlayerId()) return DamageTarget.Friendly;
			if(playerId == active.OpponentPlayerId || entity.Id == active.OpponentEntityId) return DamageTarget.Opponent;
			return DamageTarget.None;
		}

		private static int GetLocalPlayerId()
		{
			var game = Core.Game; if(game == null) return 0;
			var tagged = game.PlayerEntity?.GetTag(GameTag.PLAYER_ID) ?? 0; return tagged > 0 ? tagged : game.Player.Id;
		}

		private static int? Durability(Entity? hero) => hero == null ? (int?)null : hero.Health + hero.GetTag(GameTag.ARMOR);
		private static Entity? FindHero(int playerId)
		{
			if(playerId <= 0) return null;
			var heroes = Core.Game?.Entities.Values.Where(x => x.IsHero && x.GetTag(GameTag.PLAYER_ID) == playerId).ToArray();
			if(heroes == null) return null;
			return heroes.FirstOrDefault(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) is > 0 and <= 8) ?? heroes.FirstOrDefault();
		}
	}

	public enum DamageTarget { None, Friendly, Opponent }
}
