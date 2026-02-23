using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Games.Runtime;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class PointerTelemetryService : MonoBehaviour
    {
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private string _eventName = "pointer_shot";
        [SerializeField] private bool _logTelemetry = false;
        [SerializeField] private GameTelemetryService _gameTelemetryService;

        public static PointerTelemetryService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<PointerTelemetryService>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("PointerTelemetryService");
            host.AddComponent<PointerTelemetryService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveDependencies();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RecordShot(IReadOnlyDictionary<string, object> payload)
        {
            if (!_emitTelemetry || string.IsNullOrWhiteSpace(_eventName))
            {
                return;
            }

            ResolveDependencies();
            if (_gameTelemetryService == null)
            {
                return;
            }

            var safePayload = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();
            if (!safePayload.ContainsKey("sourceComponent"))
            {
                safePayload["sourceComponent"] = "QuestPointerClickInteractor";
            }

            var interactionBridge = InteractionEventBridge.Instance;
            if (interactionBridge != null)
            {
                interactionBridge.RecordPointerShot(safePayload);
            }

            _gameTelemetryService.Track(_eventName, safePayload);

            if (_logTelemetry)
            {
                Logger.Info($"[PointerTelemetry] {_eventName} tracked (keys={safePayload.Count}).");
            }
        }

        private void ResolveDependencies()
        {
            if (_gameTelemetryService == null)
            {
                _gameTelemetryService = FindFirstObjectByType<GameTelemetryService>();
            }
        }
    }
}
