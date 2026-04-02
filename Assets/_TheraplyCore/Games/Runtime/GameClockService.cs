using System;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Clock service used by games for timing.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameClockService : MonoBehaviour, GameContracts.IGameClock
    {
        [SerializeField] private GameSessionContext _sessionContext;
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
                _sessionContext = GetComponent<GameSessionContext>();
            }

            ResetClock();
        }

        public void ResetClock()
        {
            _fallbackStartRealtime = Time.realtimeSinceStartup;
        }
    }
}
