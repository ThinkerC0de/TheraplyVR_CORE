using System;
using UnityEngine;
using TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Clock service used by mini-games for timing.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameClockService : MonoBehaviour, IGameClock
    {
        [SerializeField] private MiniGameSessionContext _sessionContext;
        [SerializeField] private bool _syncToSessionStart = true;

        private float _fallbackStartRealtime;

        public float ElapsedSeconds
        {
            get
            {
                if (_syncToSessionStart && _sessionContext != null && _sessionContext.StartedAtUtc != default)
                {
                    return Mathf.Max(0f, (float)(DateTime.UtcNow - _sessionContext.StartedAtUtc).TotalSeconds);
                }

                return Mathf.Max(0f, Time.realtimeSinceStartup - _fallbackStartRealtime);
            }
        }

        private void Awake()
        {
            if (_sessionContext == null)
            {
                _sessionContext = GetComponent<MiniGameSessionContext>();
            }

            ResetClock();
        }

        public void ResetClock()
        {
            _fallbackStartRealtime = Time.realtimeSinceStartup;
        }
    }
}
