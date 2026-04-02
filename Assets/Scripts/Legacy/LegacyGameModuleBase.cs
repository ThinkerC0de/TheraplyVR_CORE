using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;

/// <summary>
/// Adapter base for legacy Playground games.
/// Wraps CommunicationAbstractClass communicator into the CORE IGameModule contract.
///
/// Pattern:
///   1. CORE calls Initialize() + StartGame() when mobile sends START_GAME
///   2. StartGame() forwards gameConfigJson to legacy communicator.GetJsonFromPreviewApp()
///   3. Legacy game finishes → communicator.OnGameFinished() → OnLegacyGameFinished callback
///   4. Adapter calls StopGame(Completed) → CORE emits QUEST_GAME_EVENT
///
/// Game managers are NOT touched. Only the communicator bridge is adapted.
/// WebSocketClientV6 path still works in parallel until FAZA 5.X.
/// </summary>
public interface ILegacyPauseHandler
{
    void PauseLegacyGame();
    void ResumeLegacyGame();
}

public abstract class LegacyGameModuleBase : GameModuleBase, GameContracts.IStartCommandConfigProvider
{
    private LegacyGameConfig _legacyConfig;
    private string _finishedPayload;

    /// <summary>Return the legacy communicator for this scene.</summary>
    protected abstract CommunicationAbstractClass GetCommunicator();

    public override void Initialize(GameContracts.IGameConfig config, GameContracts.IGameContext context)
    {
        _legacyConfig = config as LegacyGameConfig;
        _finishedPayload = null;

        var comm = GetCommunicator();
        if (comm != null)
            comm.OnGameFinishedCallback = OnLegacyGameFinished;

        base.Initialize(config, context);
    }

    public override void StartGame()
    {
        var isResume = State == GameContracts.GameState.Paused;
        base.StartGame();

        if (isResume)
        {
            return;
        }

        var comm = GetCommunicator();
        if (comm == null)
        {
            Debug.LogWarning($"[{GameId}] LegacyGameModuleBase: communicator is null — game cannot start.");
            return;
        }

        var json = _legacyConfig?.RawJson ?? string.Empty;
        comm.GetJsonFromPreviewApp(json);
    }

    public override void PauseGame()
    {
        var wasPlaying = State == GameContracts.GameState.Playing;
        base.PauseGame();
        if (!wasPlaying || State != GameContracts.GameState.Paused)
        {
            return;
        }

        ResolvePauseHandler()?.PauseLegacyGame();
    }

    public override void ResumeGame()
    {
        if (State != GameContracts.GameState.Paused)
        {
            base.ResumeGame();
            return;
        }

        base.StartGame();
        ResolvePauseHandler()?.ResumeLegacyGame();
    }

    public override void StopGame(GameContracts.GameStopReason reason)
    {
        var comm = GetCommunicator();
        if (comm != null)
            comm.OnGameFinishedCallback = null;

        base.StopGame(reason);
    }

    public override GameContracts.IGameResult BuildResult()
    {
        var metrics = new Dictionary<string, object>
        {
            { "state", State.ToString() },
            { "durationSec", GetDurationSeconds() },
            { "legacyPayload", _finishedPayload ?? string.Empty },
        };

        return new GameResult(
            GameId,
            State == GameContracts.GameState.Completed,
            GetDurationSeconds(),
            metrics);
    }

    public bool TryCreateConfigFromStartCommand(
        GameContracts.StartGameCommand command,
        GameContracts.IGameConfig previousConfig,
        out GameContracts.IGameConfig resolvedConfig,
        out string reasonCode)
    {
        resolvedConfig = new LegacyGameConfig(command.gameId, command.gameConfigJson);
        reasonCode = "LEGACY_CONFIG_OK";
        return true;
    }

    // -------------------------------------------------------
    private void OnLegacyGameFinished(string msg)
    {
        _finishedPayload = msg;
        if (State == GameContracts.GameState.Playing || State == GameContracts.GameState.Paused)
            StopGame(GameContracts.GameStopReason.Completed);
    }

    private ILegacyPauseHandler ResolvePauseHandler()
    {
        return GetCommunicator() as ILegacyPauseHandler;
    }
}

public static class LegacyInteractionTelemetry
{
    private static bool _missingBridgeLogged;

    public static string CreateTargetInstanceId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string CurrentUtcIso()
    {
        return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    public static float CurrentRealtimeSec()
    {
        return Time.realtimeSinceStartup;
    }

    public static float ComputeResponseSec(float targetAppearedAtElapsedSec)
    {
        if (targetAppearedAtElapsedSec <= 0f)
        {
            return 0f;
        }

        return Mathf.Max(0f, Time.realtimeSinceStartup - targetAppearedAtElapsedSec);
    }

    public static void AddTargetTimingDetails(
        IDictionary<string, object> details,
        string targetCategory,
        string targetInstanceId,
        string targetAppearedAtUtc,
        float targetAppearedAtElapsedSec,
        float? responseSec = null)
    {
        if (details == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetCategory))
        {
            details["targetCategory"] = targetCategory;
        }

        if (!string.IsNullOrWhiteSpace(targetInstanceId))
        {
            details["targetInstanceId"] = targetInstanceId;
        }

        if (!string.IsNullOrWhiteSpace(targetAppearedAtUtc))
        {
            details["targetAppearedAtUtc"] = targetAppearedAtUtc;
        }

        if (targetAppearedAtElapsedSec >= 0f)
        {
            details["targetAppearedAtElapsedSec"] = targetAppearedAtElapsedSec;
        }

        if (responseSec.HasValue)
        {
            details["responseSec"] = Mathf.Max(0f, responseSec.Value);
        }
    }

    public static void EmitOutcome(
        string gameId,
        string eventType,
        string gameState,
        string actionOutcome,
        string reasonCode,
        string sourceComponent,
        string targetId = "",
        string targetName = "",
        string inputHand = "",
        string inputSource = "",
        string inputControl = "",
        float? inputValue = null,
        IReadOnlyDictionary<string, object> extraDetails = null)
    {
        var bridge = ResolveBridge();
        if (bridge == null)
        {
            return;
        }

        var payload = extraDetails != null
            ? new Dictionary<string, object>(extraDetails)
            : new Dictionary<string, object>();

        payload["gameId"] = gameId ?? string.Empty;
        payload["actionOutcome"] = actionOutcome ?? string.Empty;
        payload["reasonCode"] = reasonCode ?? string.Empty;
        payload["payloadVersion"] = 1;

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            payload["targetId"] = targetId;
        }

        if (!string.IsNullOrWhiteSpace(targetName))
        {
            payload["targetName"] = targetName;
        }

        if (!string.IsNullOrWhiteSpace(inputHand))
        {
            payload["inputHand"] = inputHand;
        }

        if (!string.IsNullOrWhiteSpace(inputSource))
        {
            payload["inputSource"] = inputSource;
        }

        if (!string.IsNullOrWhiteSpace(inputControl))
        {
            payload["inputControl"] = inputControl;
        }

        if (inputValue.HasValue)
        {
            payload["inputValue"] = inputValue.Value;
        }

        bridge.RecordGameplayEvent(
            gameId,
            eventType,
            string.IsNullOrWhiteSpace(gameState) ? "PLAYING" : gameState,
            payload,
            string.IsNullOrWhiteSpace(sourceComponent) ? nameof(LegacyInteractionTelemetry) : sourceComponent);
    }

    private static InteractionEventBridge ResolveBridge()
    {
        var bridge = InteractionEventBridge.Instance
            ?? UnityEngine.Object.FindFirstObjectByType<InteractionEventBridge>();

        if (bridge == null)
        {
            if (!_missingBridgeLogged)
            {
                Debug.LogWarning(
                    "[LegacyInteractionTelemetry] InteractionEventBridge not found. Legacy interactions will not be published.");
                _missingBridgeLogged = true;
            }

            return null;
        }

        _missingBridgeLogged = false;
        return bridge;
    }
}

public static class PiniataZoneTruthTelemetry
{
    public const bool Enabled = false;
    private const float BodyRepeatCooldownSeconds = 0.18f;
    private const float CorrectPointBodySuppressBase = 0.40f;
    private const float CorrectPointBodySuppressMax = 1.20f;
    private static readonly Dictionary<string, float> _recentBodyContacts = new Dictionary<string, float>();
    private static float _suppressBodyMissUntil = -10f;

    public static void RegisterCorrectPointInteraction(float impactVelocity = 0f)
    {
        // Scale suppression window with impact velocity so hard hits don't bleed into a body miss.
        // v=0 → 0.40 s, v=1 → 0.55 s, v=2 → 0.70 s, v=4 → 1.00 s, v=5+ → 1.20 s (capped)
        var suppressSeconds = Mathf.Clamp(
            CorrectPointBodySuppressBase + impactVelocity * 0.20f,
            CorrectPointBodySuppressBase,
            CorrectPointBodySuppressMax);

        _suppressBodyMissUntil = Mathf.Max(
            _suppressBodyMissUntil,
            Time.unscaledTime + suppressSeconds);

        TheraplyCore.Interactions.PhysicsContactHitReporter.SuppressGroup(
            "piniata_body",
            suppressSeconds);
    }

    public static bool ShouldSuppressBodyInteraction()
    {
        return Time.unscaledTime < _suppressBodyMissUntil;
    }

    public static bool SupportsZone(string semanticTag)
    {
        var normalizedSemanticTag = Normalize(semanticTag, string.Empty);
        return string.Equals(normalizedSemanticTag, "POINT_TARGET", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedSemanticTag, "PINIATA_BODY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedSemanticTag, "PINIATA_BUG", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryBeginBodyInteraction(TargetValidationZone zone, Collider stickCollider)
    {
        if (zone == null)
        {
            return false;
        }

        var stickKey = stickCollider == null
            ? "NO_STICK"
            : stickCollider.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        var token = Normalize(zone.TargetId, zone.gameObject.name) + "::" + stickKey;
        var now = Time.unscaledTime;

        if (_recentBodyContacts.TryGetValue(token, out var lastSeenAt) &&
            now - lastSeenAt < BodyRepeatCooldownSeconds)
        {
            return false;
        }

        _recentBodyContacts[token] = now;
        return true;
    }

    public static bool TryEmitZoneOutcome(
        Collider targetCollider,
        PiniataGame game,
        HittingStick.HandType handType,
        string stickTag,
        string sourceComponent,
        float? impactMagnitude = null)
    {
        if (!Enabled || targetCollider == null || game == null)
        {
            return false;
        }

        if (!TargetValidationZone.TryResolve(targetCollider, out var zone))
        {
            return false;
        }

        var semanticTag = Normalize(zone.SemanticTag, "UNKNOWN_TARGET");
        if (string.Equals(semanticTag, "POINT_TARGET", StringComparison.OrdinalIgnoreCase))
        {
            return TryEmitPointOutcome(targetCollider, zone, game, handType, stickTag, sourceComponent, impactMagnitude);
        }

        if (string.Equals(semanticTag, "PINIATA_BUG", StringComparison.OrdinalIgnoreCase))
        {
            return TryEmitBugOutcome(targetCollider, zone, game, handType, stickTag, sourceComponent, impactMagnitude);
        }

        return false;
    }

    private static bool TryEmitPointOutcome(
        Collider targetCollider,
        TargetValidationZone zone,
        PiniataGame game,
        HittingStick.HandType handType,
        string stickTag,
        string sourceComponent,
        float? impactMagnitude)
    {
        var hitPoint = targetCollider.GetComponent<HitPoint>() ?? targetCollider.GetComponentInParent<HitPoint>();
        if (hitPoint == null || !hitPoint.IsShown || hitPoint.PointTouched)
        {
            return true;
        }

        var isCorrect = IsMatchingStickForPoint(stickTag, hitPoint.PointType);
        if (isCorrect)
        {
            RegisterCorrectPointInteraction(impactMagnitude ?? 0f);
        }

        var actionOutcome = isCorrect ? "CORRECT" : "INCORRECT";
        var reasonCode = isCorrect ? "PINIATA_TARGET_MATCH" : "PINIATA_TARGET_MISMATCH";
        var eventType = isCorrect ? "piniata_point_hit" : "piniata_point_hit_invalid";
        var responseSec = hitPoint.TimeElapsed;

        var details = new Dictionary<string, object>
        {
            { "pointType", hitPoint.PointType.ToString() },
            { "pointTouched", hitPoint.PointTouched },
            { "targetCategory", "PINIATA_POINT" },
            { "targetSemanticTag", Normalize(zone.SemanticTag, "POINT_TARGET") },
            { "reactionSec", responseSec },
        };

        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            "PINIATA_POINT",
            hitPoint.TargetInstanceId,
            hitPoint.TargetAppearedAtUtc,
            hitPoint.TargetAppearedAtElapsedSec,
            responseSec);

        LegacyInteractionTelemetry.EmitOutcome(
            "piniata",
            eventType,
            game.State.ToString(),
            actionOutcome,
            reasonCode,
            Normalize(sourceComponent, nameof(PiniataZoneTruthTelemetry)),
            targetId: Normalize(zone.TargetId, targetCollider.GetInstanceID().ToString()),
            targetName: Normalize(zone.gameObject.name, targetCollider.gameObject.name),
            inputHand: ResolveInputHand(handType),
            inputSource: Normalize(stickTag, string.Empty),
            inputValue: impactMagnitude ?? responseSec,
            extraDetails: details);

        return true;
    }

    public static bool EmitBodyOutcome(
        Collider targetCollider,
        TargetValidationZone zone,
        PiniataGame game,
        HittingStick.HandType handType,
        string stickTag,
        string sourceComponent,
        float? impactMagnitude,
        string bodySource)
    {
        if (!Enabled || targetCollider == null || zone == null || game == null)
        {
            return false;
        }

        var bodyTelemetry = targetCollider.GetComponentInParent<Jakub.CollisionSound>();
        var targetInstanceId = bodyTelemetry != null ? bodyTelemetry.BodyTargetInstanceId : string.Empty;
        var targetAppearedAtUtc = bodyTelemetry != null ? bodyTelemetry.BodyAppearedAtUtc : string.Empty;
        var targetAppearedAtElapsedSec = bodyTelemetry != null ? bodyTelemetry.BodyAppearedAtElapsedSec : -1f;
        var semanticTag = Normalize(zone.SemanticTag, "PINIATA_BODY");

        var details = new Dictionary<string, object>
        {
            { "targetCategory", semanticTag },
            { "targetSemanticTag", semanticTag },
            { "bodyColliderName", targetCollider.gameObject.name },
            { "bodyHitSource", Normalize(bodySource, string.Empty) },
        };

        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            semanticTag,
            targetInstanceId,
            targetAppearedAtUtc,
            targetAppearedAtElapsedSec,
            LegacyInteractionTelemetry.ComputeResponseSec(targetAppearedAtElapsedSec));

        LegacyInteractionTelemetry.EmitOutcome(
            "piniata",
            "piniata_body_hit",
            game.State.ToString(),
            "INCORRECT",
            "PINIATA_BODY_HIT",
            Normalize(sourceComponent, nameof(PiniataZoneTruthTelemetry)),
            targetId: Normalize(zone.TargetId, targetCollider.GetInstanceID().ToString(CultureInfo.InvariantCulture)),
            targetName: Normalize(zone.gameObject.name, targetCollider.gameObject.name),
            inputHand: ResolveInputHand(handType),
            inputSource: Normalize(stickTag, string.Empty),
            inputValue: impactMagnitude,
            extraDetails: details);

        return true;
    }

    private static bool TryEmitBugOutcome(
        Collider targetCollider,
        TargetValidationZone zone,
        PiniataGame game,
        HittingStick.HandType handType,
        string stickTag,
        string sourceComponent,
        float? impactMagnitude)
    {
        var disturbBug = targetCollider.GetComponent<DisturbBug>() ?? targetCollider.GetComponentInParent<DisturbBug>();
        if (disturbBug == null || disturbBug.IsHit)
        {
            return true;
        }

        var details = new Dictionary<string, object>
        {
            { "targetCategory", "DISTURB_BUG" },
            { "targetSemanticTag", Normalize(zone.SemanticTag, "PINIATA_BUG") },
        };

        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            "DISTURB_BUG",
            disturbBug.TargetInstanceId,
            disturbBug.TargetAppearedAtUtc,
            disturbBug.TargetAppearedAtElapsedSec,
            LegacyInteractionTelemetry.ComputeResponseSec(disturbBug.TargetAppearedAtElapsedSec));

        LegacyInteractionTelemetry.EmitOutcome(
            "piniata",
            "piniata_bug_hit",
            game.State.ToString(),
            "INCORRECT",
            "PINIATA_BUG_TRIGGERED",
            Normalize(sourceComponent, nameof(PiniataZoneTruthTelemetry)),
            targetId: Normalize(zone.TargetId, targetCollider.GetInstanceID().ToString()),
            targetName: Normalize(zone.gameObject.name, targetCollider.gameObject.name),
            inputHand: ResolveInputHand(handType),
            inputSource: Normalize(stickTag, "DISTURB_BUG"),
            inputValue: impactMagnitude,
            extraDetails: details);

        return true;
    }

    private static bool IsMatchingStickForPoint(string stickTag, PiniataGame.HitPointType pointType)
    {
        if (string.Equals(stickTag, "Stick_Red", StringComparison.OrdinalIgnoreCase))
        {
            return pointType == PiniataGame.HitPointType.Red;
        }

        if (string.Equals(stickTag, "Stick_Blue", StringComparison.OrdinalIgnoreCase))
        {
            return pointType == PiniataGame.HitPointType.Blue;
        }

        return false;
    }

    private static string ResolveInputHand(HittingStick.HandType handType)
    {
        switch (handType)
        {
            case HittingStick.HandType.LeftHand:
                return "LEFT";
            case HittingStick.HandType.RightHand:
                return "RIGHT";
            default:
                return string.Empty;
        }
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? (fallback ?? string.Empty)
            : value.Trim();
    }
}

public sealed class PiniataZoneTruthBootstrap : MonoBehaviour
{
    private const float ScanIntervalSeconds = 1f;
    private float _nextScanAt;

    private void Awake()
    {
        AttachReporters();
        _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        if (!PiniataZoneTruthTelemetry.Enabled)
        {
            return;
        }

        if (FindFirstObjectByType<PiniataZoneTruthBootstrap>() != null)
        {
            return;
        }

        var host = new GameObject("[PiniataZoneTruthBootstrap]");
        DontDestroyOnLoad(host);
        host.AddComponent<PiniataZoneTruthBootstrap>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScanAt)
        {
            return;
        }

        _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
        AttachReporters();
    }

    private static void AttachReporters()
    {
        var colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var index = 0; index < colliders.Length; index++)
        {
            var colliderRef = colliders[index];
            if (colliderRef == null)
            {
                continue;
            }

            if (!TargetValidationZone.TryResolve(colliderRef, out var zone) ||
                !PiniataZoneTruthTelemetry.SupportsZone(zone.SemanticTag))
            {
                continue;
            }

            var reporter = colliderRef.GetComponent<PiniataZoneTruthTargetReporter>();
            if (reporter == null)
            {
                reporter = colliderRef.gameObject.AddComponent<PiniataZoneTruthTargetReporter>();
            }

            reporter.Initialize(zone);
        }
    }
}

public sealed class PiniataZoneTruthTargetReporter : MonoBehaviour
{
    private const float BodyDecisionDelaySeconds = 0.06f;
    private const float BodyImpactThreshold = 0.08f;

    [SerializeField] private TargetValidationZone _zone;

    private Collider _targetCollider;
    private HitPoint _hitPoint;
    private DisturbBug _disturbBug;
    private PiniataGame _game;
    private Coroutine _pendingBodyRoutine;
    private Collider _pendingStickCollider;
    private HittingStick.HandType _pendingHandType;
    private string _pendingStickTag = string.Empty;
    private float _pendingImpactMagnitude;
    private string _pendingBodySource = string.Empty;

    public void Initialize(TargetValidationZone zone)
    {
        _zone = zone;
        CacheReferences();
    }

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
    }

    private void OnDisable()
    {
        if (_pendingBodyRoutine != null)
        {
            StopCoroutine(_pendingBodyRoutine);
            _pendingBodyRoutine = null;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
        {
            return;
        }

        TryHandleContact(
            collision.collider,
            collision.relativeVelocity.magnitude,
            "target_collision_enter",
            isEnter: true);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHandleContact(
            other,
            EstimateStickImpactMagnitude(other),
            "target_trigger_enter",
            isEnter: true);
    }

    private void TryHandleContact(
        Collider stickCollider,
        float? impactMagnitude,
        string source,
        bool isEnter)
    {
        if (!PiniataZoneTruthTelemetry.Enabled || !isEnter)
        {
            return;
        }

        CacheReferences();
        if (_targetCollider == null || _zone == null || !IsGameReady())
        {
            return;
        }

        if (!TryResolveStick(stickCollider, out var hittingStick, out var stickTag))
        {
            return;
        }

        var semanticTag = Normalize(_zone.SemanticTag);
        if (string.Equals(semanticTag, "PINIATA_BODY", StringComparison.OrdinalIgnoreCase))
        {
            QueueBodyOutcome(stickCollider, hittingStick.handType, stickTag, impactMagnitude, source);
            return;
        }

        PiniataZoneTruthTelemetry.TryEmitZoneOutcome(
            _targetCollider,
            _game,
            hittingStick.handType,
            stickTag,
            nameof(PiniataZoneTruthTargetReporter),
            impactMagnitude);
    }

    private void QueueBodyOutcome(
        Collider stickCollider,
        HittingStick.HandType handType,
        string stickTag,
        float? impactMagnitude,
        string source)
    {
        if (!HasMeaningfulBodyImpact(impactMagnitude))
        {
            return;
        }

        _pendingStickCollider = stickCollider;
        _pendingHandType = handType;
        _pendingStickTag = stickTag ?? string.Empty;
        _pendingImpactMagnitude = Mathf.Max(_pendingImpactMagnitude, impactMagnitude ?? 0f);
        _pendingBodySource = source ?? string.Empty;

        if (_pendingBodyRoutine == null)
        {
            _pendingBodyRoutine = StartCoroutine(FlushPendingBodyOutcome());
        }
    }

    private IEnumerator FlushPendingBodyOutcome()
    {
        yield return new WaitForSecondsRealtime(BodyDecisionDelaySeconds);

        var stickCollider = _pendingStickCollider;
        var handType = _pendingHandType;
        var stickTag = _pendingStickTag;
        var impactMagnitude = _pendingImpactMagnitude;
        var bodySource = _pendingBodySource;

        _pendingBodyRoutine = null;
        _pendingStickCollider = null;
        _pendingHandType = HittingStick.HandType.NotSelected;
        _pendingStickTag = string.Empty;
        _pendingImpactMagnitude = 0f;
        _pendingBodySource = string.Empty;

        if (_targetCollider == null || _zone == null || !IsGameReady() || PiniataZoneTruthTelemetry.ShouldSuppressBodyInteraction())
        {
            yield break;
        }

        if (!PiniataZoneTruthTelemetry.TryBeginBodyInteraction(_zone, stickCollider))
        {
            yield break;
        }

        if (PiniataZoneTruthTelemetry.EmitBodyOutcome(
                _targetCollider,
                _zone,
                _game,
                handType,
                stickTag,
                nameof(PiniataZoneTruthTargetReporter),
                impactMagnitude,
                bodySource))
        {
            Debug.Log(
                "[PiniataZoneTruthTargetReporter] EmitBodyOutcome target=" +
                Normalize(_zone.TargetId) +
                " collider=" + _targetCollider.gameObject.name +
                " impact=" + impactMagnitude.ToString("F3", CultureInfo.InvariantCulture) +
                " source=" + Normalize(bodySource));
        }
    }

    private void CacheReferences()
    {
        if (_targetCollider == null)
        {
            _targetCollider = GetComponent<Collider>();
        }

        if (_zone == null && _targetCollider != null)
        {
            TargetValidationZone.TryResolve(_targetCollider, out _zone);
        }

        if (_hitPoint == null)
        {
            _hitPoint = GetComponent<HitPoint>() ?? GetComponentInParent<HitPoint>();
        }

        if (_disturbBug == null)
        {
            _disturbBug = GetComponent<DisturbBug>() ?? GetComponentInParent<DisturbBug>();
        }

        if (_game == null)
        {
            _game = FindFirstObjectByType<PiniataGame>();
        }
    }

    private bool IsGameReady()
    {
        if (_game == null)
        {
            _game = FindFirstObjectByType<PiniataGame>();
        }

        return _game != null &&
               !_game.IsGameplayPaused &&
               _game.State == PiniataGame.GameState.InProgress;
    }

    private static bool TryResolveStick(
        Collider stickCollider,
        out HittingStick hittingStick,
        out string stickTag)
    {
        hittingStick = null;
        stickTag = string.Empty;

        if (stickCollider == null)
        {
            return false;
        }

        hittingStick = stickCollider.GetComponentInParent<HittingStick>();
        if (hittingStick == null)
        {
            return false;
        }

        stickTag = ResolveStickTag(stickCollider.transform);
        return !string.IsNullOrWhiteSpace(stickTag);
    }

    private static string ResolveStickTag(Transform current)
    {
        while (current != null)
        {
            if (current.CompareTag("Stick_Red"))
            {
                return "Stick_Red";
            }

            if (current.CompareTag("Stick_Blue"))
            {
                return "Stick_Blue";
            }

            current = current.parent;
        }

        return string.Empty;
    }

    private static float EstimateStickImpactMagnitude(Collider stickCollider)
    {
        if (stickCollider == null)
        {
            return 0f;
        }

        var attachedBody = stickCollider.attachedRigidbody ?? stickCollider.GetComponentInParent<Rigidbody>();
        return attachedBody == null ? 0f : attachedBody.linearVelocity.magnitude;
    }

    private static bool HasMeaningfulBodyImpact(float? impactMagnitude)
    {
        return impactMagnitude.HasValue && impactMagnitude.Value >= BodyImpactThreshold;
    }

    private static string Normalize(string value, string fallback = "")
    {
        return string.IsNullOrWhiteSpace(value)
            ? (fallback ?? string.Empty)
            : value.Trim();
    }
}
