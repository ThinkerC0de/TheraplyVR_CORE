using System;
using TheraplyCore.Firebase;
using TheraplyCore.Games.Runtime;
using UnityEngine;

namespace TheraplyExamples
{
    /// <summary>
    /// Lightweight runtime diagnostics HUD for Editor smoke and operator demos.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EditorRuntimeDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private bool _showOverlay = true;
        [SerializeField] private Rect _overlayRect = new Rect(12f, 148f, 420f, 120f);
        [SerializeField] [Range(0.1f, 2f)] private float _refreshIntervalSeconds = 0.5f;

        private GameRuntimeService _runtimeService;
        private GameSessionContext _sessionContext;
        private FirebaseDataService _firebaseDataService;

        private float _nextRefreshAt;
        private string _cachedText = string.Empty;
        private GUIStyle _labelStyle;

        private void Awake()
        {
            ResolveDependencies();
            RefreshDiagnostics(force: true);
        }

        private void Update()
        {
            if (!_showOverlay)
            {
                return;
            }

            RefreshDiagnostics(force: false);
        }

        private void OnGUI()
        {
            if (!_showOverlay || string.IsNullOrWhiteSpace(_cachedText))
            {
                return;
            }

            GUI.Box(_overlayRect, GUIContent.none);
            var labelRect = new Rect(
                _overlayRect.x + 10f,
                _overlayRect.y + 8f,
                _overlayRect.width - 20f,
                _overlayRect.height - 16f);
            GUI.Label(labelRect, _cachedText, LabelStyle);
        }

        private GUIStyle LabelStyle
        {
            get
            {
                if (_labelStyle != null)
                {
                    return _labelStyle;
                }

                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                };
                return _labelStyle;
            }
        }

        private void ResolveDependencies()
        {
            if (_runtimeService == null)
            {
                _runtimeService = FindFirstObjectByType<GameRuntimeService>();
            }

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }

            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }
        }

        private void RefreshDiagnostics(bool force)
        {
            var now = Time.unscaledTime;
            if (!force && now < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = now + Mathf.Max(0.1f, _refreshIntervalSeconds);
            ResolveDependencies();

            var sessionId = _sessionContext == null || string.IsNullOrWhiteSpace(_sessionContext.SessionId)
                ? "none"
                : _sessionContext.SessionId;
            var sessionState = _sessionContext == null ? "UNKNOWN" : _sessionContext.SessionState.ToString();
            var activeGameId = _runtimeService == null || string.IsNullOrWhiteSpace(_runtimeService.ActiveGameId)
                ? "none"
                : _runtimeService.ActiveGameId;
            var runtimeStatus = _runtimeService == null || string.IsNullOrWhiteSpace(_runtimeService.LastPublishedRuntimeStatus)
                ? "unknown"
                : _runtimeService.LastPublishedRuntimeStatus;

            var backendState = "missing";
            var pendingOutbox = 0;
            var outboxFailures = 0;
            if (_firebaseDataService != null)
            {
                var stats = _firebaseDataService.GetStatistics();
                pendingOutbox = stats.durableOutboxPending;
                outboxFailures = stats.outboxSyncFailures;

                if (outboxFailures > 0)
                {
                    backendState = "degraded";
                }
                else if (pendingOutbox > 0)
                {
                    backendState = "sync_pending";
                }
                else
                {
                    backendState = "online";
                }
            }

            _cachedText =
                "Runtime Diagnostics (Editor)\n" +
                $"Session ID: {sessionId}\n" +
                $"Session State: {sessionState}\n" +
                $"Active Game ID: {activeGameId}\n" +
                $"Runtime Status: {runtimeStatus}\n" +
                $"Backend: {backendState} (outbox pending={pendingOutbox}, sync failures={outboxFailures})";
        }
    }
}
