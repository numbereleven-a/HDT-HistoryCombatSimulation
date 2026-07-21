namespace HistoryCombatSimulation
{
	public sealed class SoloMatchReentryGate
	{
		private bool _blockedAfterMatchEnd;

		public bool ShouldTrack(bool isSoloBattlegrounds)
		{
			if(!isSoloBattlegrounds) { _blockedAfterMatchEnd = false; return false; }
			return !_blockedAfterMatchEnd;
		}

		public void MatchEnded() => _blockedAfterMatchEnd = true;
		public void GameStarted() => _blockedAfterMatchEnd = false;
	}
}
