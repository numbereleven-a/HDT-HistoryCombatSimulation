namespace HistoryCombatSimulation
{
	public enum GameStartDecision { None, Wait, ContinueExistingMatch, StartNewMatch }

	public sealed class ReconnectGameStartGate
	{
		private readonly long _decisionDelayMilliseconds;
		private bool _pending;
		private long? _soloReadySince;

		public ReconnectGameStartGate(long decisionDelayMilliseconds = 10000) => _decisionDelayMilliseconds = decisionDelayMilliseconds;
		public bool IsPending => _pending;
		public void Reset() { _pending = false; _soloReadySince = null; }
		public void Notify() { _pending = true; _soloReadySince = null; }

		public GameStartDecision Resolve(bool isSoloBattlegrounds, bool isReconnect, long nowMilliseconds)
		{
			if(!_pending) return GameStartDecision.None;
			if(!isSoloBattlegrounds) { _soloReadySince = null; return GameStartDecision.Wait; }
			if(isReconnect) { _pending = false; _soloReadySince = null; return GameStartDecision.ContinueExistingMatch; }
			if(!_soloReadySince.HasValue) { _soloReadySince = nowMilliseconds; return GameStartDecision.Wait; }
			if(nowMilliseconds - _soloReadySince.Value < _decisionDelayMilliseconds) return GameStartDecision.Wait;
			_pending = false; _soloReadySince = null; return GameStartDecision.StartNewMatch;
		}
	}
}
