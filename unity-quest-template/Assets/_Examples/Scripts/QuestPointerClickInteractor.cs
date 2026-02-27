using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR;
using TheraplyCore.Interactions;
using TheraplyCore.Games.Runtime;

namespace TheraplyExamples
{
    public interface IQuestPointerTarget
    {
        void ActivateFromPointer(string source);
    }

    /// <summary>
    /// Simple Quest-friendly pointer interactor:
    /// - trigger/button press on XR controllers performs a physics raycast,
    /// - hit targets implementing IQuestPointerTarget are activated.
    /// Falls back to camera-forward ray when controller transforms are not assigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestPointerClickInteractor : MonoBehaviour
    {
        private static readonly string[] RightRayOriginCandidates =
        {
            "OVRCameraRig/TrackingSpace/RightControllerInHandAnchor",
            "OVRCameraRig/TrackingSpace/RightHandOnControllerAnchor",
            "OVRCameraRig/TrackingSpace/RightTouchControllerAnchor",
            "OVRCameraRig/TrackingSpace/RightHandAnchor",
            "OVRCameraRig/TrackingSpace/RightHandAnchorDetached",
            "XR Origin/Camera Offset/Right Controller",
            "XR Origin/Right Controller",
            "RightHand Controller",
            "Right Controller",
        };

        private static readonly string[] LeftRayOriginCandidates =
        {
            "OVRCameraRig/TrackingSpace/LeftControllerInHandAnchor",
            "OVRCameraRig/TrackingSpace/LeftHandOnControllerAnchor",
            "OVRCameraRig/TrackingSpace/LeftTouchControllerAnchor",
            "OVRCameraRig/TrackingSpace/LeftHandAnchor",
            "OVRCameraRig/TrackingSpace/LeftHandAnchorDetached",
            "XR Origin/Camera Offset/Left Controller",
            "XR Origin/Left Controller",
            "LeftHand Controller",
            "Left Controller",
        };

        [Header("Ray Source")]
        [SerializeField] private Camera _rayCamera;
        [SerializeField] private Transform _rightRayOrigin;
        [SerializeField] private Transform _leftRayOrigin;
        [SerializeField] private bool _autoDetectRayOrigins = true;
        [SerializeField] private bool _preferLeftRayOrigin = true;
        [SerializeField] private bool _switchRayOriginToPressedHand = true;
        [SerializeField] private bool _useCameraHandFallbackWhenOriginMissing = true;
        [SerializeField] private float _cameraHandFallbackHorizontalOffset = 0.14f;
        [SerializeField] private float _cameraHandFallbackVerticalOffset = -0.05f;
        [SerializeField] private float _cameraHandFallbackForwardOffset = 0.12f;

        [Header("Raycast")]
        [SerializeField] private float _maxDistance = 20f;
        [SerializeField] private LayerMask _hitMask = ~0;
        [SerializeField] private bool _includeTriggerColliders = true;
        [SerializeField] private bool _ignorePointerRigColliders = true;

        [Header("Laser Pointer")]
        [SerializeField] private bool _showLaser = true;
        [SerializeField] private bool _showDualHandLasers = true;
        [SerializeField] private bool _showLaserWhenNoHit = true;
        [SerializeField] private float _laserWidth = 0.005f;
        [SerializeField] private Color _laserColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [SerializeField] private Color _leftLaserFallbackColor = new Color(0.12f, 0.95f, 0.35f, 0.9f);
        [SerializeField] private Color _rightLaserFallbackColor = new Color(0.15f, 0.55f, 1f, 0.9f);

        [Header("Wand Indicator")]
        [SerializeField] private bool _showWand = true;
        [SerializeField] private bool _showDualHandWands = true;
        [SerializeField] private float _wandLength = 0.24f;
        [SerializeField] private float _wandRadius = 0.008f;
        [SerializeField] private float _wandTipRadius = 0.016f;
        [SerializeField] private bool _hidePointerVisualsInBilateralMarkers = false;
        [SerializeField] private string _bilateralMarkersGameId = "bilateral_markers";
        [SerializeField] private bool _colorFromSessionIndicator = true;
        [SerializeField] private Color _wandFallbackColor = new Color(0.2f, 0.9f, 1f, 0.95f);
        [SerializeField] private Color _leftWandFallbackColor = new Color(0.12f, 0.95f, 0.35f, 0.95f);
        [SerializeField] private Color _rightWandFallbackColor = new Color(0.15f, 0.55f, 1f, 0.95f);

        [Header("Telemetry")]
        [SerializeField] private bool _emitPointerTelemetry = true;
        [SerializeField] private bool _logPointerTelemetry = false;
        [SerializeField] private GameRuntimeService _gameRuntimeService;

        [Header("Tool Telemetry")]
        [SerializeField] private bool _emitToolTelemetry = true;
        [SerializeField] private string _toolId = "quest_pointer_wand";
        [SerializeField] private ToolGripTracker _toolGripTracker;
        [SerializeField] private ToolImpactProbe _toolImpactProbe;

        [Header("Debug")]
        [SerializeField] private bool _logInteractions = false;
#if UNITY_EDITOR
        [SerializeField] private bool _allowMouseFallbackInEditor = true;
#endif

        private InputDevice _rightController;
        private InputDevice _leftController;
        private bool _wasPressed;
        private float _nextControllerRefreshAtRealtime;
        private float _nextRayOriginScanAtRealtime;
        private LaserVisual _leftLaser = new LaserVisual();
        private LaserVisual _rightLaser = new LaserVisual();
        private WandVisual _leftWand = new WandVisual();
        private WandVisual _rightWand = new WandVisual();
        private string _lastActivationHand = "UNKNOWN";
        private string _lastActivationControl = "UNKNOWN";
        private float _lastActivationValue = 0f;

        private sealed class LaserVisual
        {
            public LineRenderer line;
            public Material material;
        }

        private sealed class WandVisual
        {
            public Transform body;
            public Transform tip;
            public Material bodyMaterial;
            public Material tipMaterial;
        }

        private struct PointerShotRecord
        {
            public bool HitAnyCollider;
            public bool HitInteractiveTarget;
            public bool HasHoverFeedback;
            public bool HoverIsValidTarget;
            public string HitObjectName;
            public float HitDistance;
            public Color IndicatorColor;
            public Collider HitCollider;
            public Vector3 HitPoint;
            public Vector3 HitNormal;
            public TargetValidationZone ValidationZone;
        }

        private void Awake()
        {
            if (_rayCamera == null)
            {
                _rayCamera = Camera.main;
            }

            if (_gameRuntimeService == null)
            {
                _gameRuntimeService = FindFirstObjectByType<GameRuntimeService>();
            }

            ResolveToolTelemetryDependencies();
            EnsureLaserRenderers();
            EnsureWandVisuals();
        }

        private void Update()
        {
            RefreshControllersIfNeeded();
            if (IsPointerSuppressedForActiveGame())
            {
                HidePointerVisuals();
                ReleaseToolGripIfNeeded();
                _wasPressed = false;
                return;
            }

            UpdateLaserVisual();
            UpdateWandVisual();

            var isPressed = IsActivationPressed();
            EmitToolGripTelemetry(isPressed);
            var justPressed = isPressed && !_wasPressed;
            _wasPressed = isPressed;

            if (!justPressed)
            {
                return;
            }

            var shot = TryActivateHitTarget();
            EmitPointerTelemetry(shot);
            EmitToolImpactTelemetry(shot);
        }

        private void OnDisable()
        {
            ReleaseToolGripIfNeeded();
        }

        private void OnDestroy()
        {
            ReleaseToolGripIfNeeded();

            DestroyLaserVisual(_leftLaser);
            DestroyLaserVisual(_rightLaser);

            DestroyWandVisual(_leftWand);
            DestroyWandVisual(_rightWand);
        }

        private bool IsActivationPressed()
        {
            if (TryReadControllerPress(_rightController, "RIGHT", out var rightControl, out var rightValue))
            {
                SetActivationContext("RIGHT", rightControl, rightValue);
                return true;
            }

            if (TryReadControllerPress(_leftController, "LEFT", out var leftControl, out var leftValue))
            {
                SetActivationContext("LEFT", leftControl, leftValue);
                return true;
            }

#if UNITY_EDITOR
#if ENABLE_INPUT_SYSTEM
            if (_allowMouseFallbackInEditor &&
                UnityEngine.InputSystem.Mouse.current != null &&
                UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetActivationContext("EDITOR", "MOUSE_LEFT", 1f);
                return true;
            }
#else
            if (_allowMouseFallbackInEditor && Input.GetMouseButtonDown(0))
            {
                SetActivationContext("EDITOR", "MOUSE_LEFT", 1f);
                return true;
            }
#endif
#endif
            return false;
        }

        private void RefreshControllersIfNeeded()
        {
            if (Time.realtimeSinceStartup < _nextControllerRefreshAtRealtime)
            {
                return;
            }

            _nextControllerRefreshAtRealtime = Time.realtimeSinceStartup + 1f;

            _rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            _leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

            TryAutoResolveRayOrigins();
        }

        private void TryAutoResolveRayOrigins()
        {
            if (!_autoDetectRayOrigins || Time.realtimeSinceStartup < _nextRayOriginScanAtRealtime)
            {
                return;
            }

            _nextRayOriginScanAtRealtime = Time.realtimeSinceStartup + 1f;

            var rightCandidate = FindRayOriginCandidate(true, _leftRayOrigin);
            if (_rightRayOrigin == null)
            {
                _rightRayOrigin = rightCandidate;
            }
            else if (ShouldPreferRayOriginCandidate(_rightRayOrigin, rightCandidate, "right"))
            {
                _rightRayOrigin = rightCandidate;
            }

            var leftCandidate = FindRayOriginCandidate(false, _rightRayOrigin);
            if (_leftRayOrigin == null)
            {
                _leftRayOrigin = leftCandidate;
            }
            else if (ShouldPreferRayOriginCandidate(_leftRayOrigin, leftCandidate, "left"))
            {
                _leftRayOrigin = leftCandidate;
            }

            EnsureDistinctRayOrigins();

            if (_rayCamera == null)
            {
                _rayCamera = Camera.main;
            }
        }

        private static bool ShouldPreferRayOriginCandidate(Transform current, Transform candidate, string handToken)
        {
            if (candidate == null || current == candidate)
            {
                return false;
            }

            var currentScore = ScoreRayOriginCandidate(current, handToken);
            var candidateScore = ScoreRayOriginCandidate(candidate, handToken);
            return candidateScore >= currentScore + 20;
        }

        private static bool TryReadControllerPress(
            InputDevice device,
            string hand,
            out string controlName,
            out float controlValue)
        {
            controlName = "NONE";
            controlValue = 0f;

            if (!device.isValid)
            {
                return false;
            }

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out var triggerPressed) && triggerPressed)
            {
                controlName = hand + "_TRIGGER_BUTTON";
                controlValue = 1f;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out var primaryPressed) && primaryPressed)
            {
                controlName = hand + "_PRIMARY_BUTTON";
                controlValue = 1f;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out var secondaryPressed) && secondaryPressed)
            {
                controlName = hand + "_SECONDARY_BUTTON";
                controlValue = 1f;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.gripButton, out var gripPressed) && gripPressed)
            {
                controlName = hand + "_GRIP_BUTTON";
                controlValue = 1f;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.trigger, out var triggerAxis) && triggerAxis > 0.7f)
            {
                controlName = hand + "_TRIGGER_AXIS";
                controlValue = triggerAxis;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.grip, out var gripAxis) && gripAxis > 0.7f)
            {
                controlName = hand + "_GRIP_AXIS";
                controlValue = gripAxis;
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out var axisClick) && axisClick)
            {
                controlName = hand + "_PRIMARY_AXIS_CLICK";
                controlValue = 1f;
                return true;
            }

            return false;
        }

        private void SetActivationContext(string hand, string controlName, float controlValue)
        {
            _lastActivationHand = string.IsNullOrWhiteSpace(hand) ? "UNKNOWN" : hand;
            _lastActivationControl = string.IsNullOrWhiteSpace(controlName) ? "NONE" : controlName;
            _lastActivationValue = Mathf.Clamp01(controlValue);
        }

        private PointerShotRecord TryActivateHitTarget()
        {
            if (!TryRaycastFromPointer(out _, out var hit))
            {
                return new PointerShotRecord
                {
                    HitAnyCollider = false,
                    HitInteractiveTarget = false,
                    HasHoverFeedback = false,
                    HoverIsValidTarget = false,
                    HitObjectName = string.Empty,
                    HitDistance = 0f,
                    IndicatorColor = ResolveSessionIndicatorColor(),
                    HitCollider = null,
                    HitPoint = Vector3.zero,
                    HitNormal = Vector3.zero,
                    ValidationZone = null,
                };
            }

            var hasHoverFeedback = TryResolveHoverFeedback(hit, out var hoverFeedback);
            var validationZone = hit.collider == null
                ? null
                : hit.collider.GetComponentInParent<TargetValidationZone>();
            var target = hit.collider.GetComponentInParent<IQuestPointerTarget>();
            if (target == null)
            {
                return new PointerShotRecord
                {
                    HitAnyCollider = true,
                    HitInteractiveTarget = false,
                    HasHoverFeedback = hasHoverFeedback,
                    HoverIsValidTarget = hasHoverFeedback && hoverFeedback.IsValidTarget,
                    HitObjectName = hit.collider.name,
                    HitDistance = hit.distance,
                    IndicatorColor = hasHoverFeedback ? hoverFeedback.IndicatorColor : ResolveSessionIndicatorColor(),
                    HitCollider = hit.collider,
                    HitPoint = hit.point,
                    HitNormal = hit.normal,
                    ValidationZone = validationZone,
                };
            }

            target.ActivateFromPointer(BuildPointerActivationSource());

            if (_logInteractions)
            {
                Debug.Log($"[QuestPointer] Activated target via raycast: {hit.collider.name}");
            }

            return new PointerShotRecord
            {
                HitAnyCollider = true,
                HitInteractiveTarget = true,
                HasHoverFeedback = hasHoverFeedback,
                HoverIsValidTarget = hasHoverFeedback && hoverFeedback.IsValidTarget,
                HitObjectName = hit.collider.name,
                HitDistance = hit.distance,
                IndicatorColor = hasHoverFeedback ? hoverFeedback.IndicatorColor : ResolveSessionIndicatorColor(),
                HitCollider = hit.collider,
                HitPoint = hit.point,
                HitNormal = hit.normal,
                ValidationZone = validationZone,
            };
        }

        private bool TryRaycastFromPointer(out Ray ray, out RaycastHit hit)
        {
            ray = BuildPointerRay();
            return TryRaycastFromRay(ray, out hit);
        }

        private bool TryRaycastFromRay(Ray ray, out RaycastHit hit)
        {
            hit = default;

            var triggerInteraction = _includeTriggerColliders
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;
            var hits = Physics.RaycastAll(ray, _maxDistance, _hitMask, triggerInteraction);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < hits.Length; i++)
            {
                if (ShouldIgnoreRaycastHit(hits[i]))
                {
                    continue;
                }

                hit = hits[i];
                return true;
            }

            return false;
        }

        private bool ShouldIgnoreRaycastHit(RaycastHit hit)
        {
            if (!_ignorePointerRigColliders || hit.collider == null)
            {
                return false;
            }

            var colliderTransform = hit.collider.transform;
            if (colliderTransform == null)
            {
                return false;
            }

            if (transform != null &&
                (colliderTransform == transform || colliderTransform.IsChildOf(transform)))
            {
                return true;
            }

            if (_rightRayOrigin != null &&
                (colliderTransform == _rightRayOrigin ||
                 colliderTransform.IsChildOf(_rightRayOrigin) ||
                 _rightRayOrigin.IsChildOf(colliderTransform)))
            {
                return true;
            }

            if (_leftRayOrigin != null &&
                (colliderTransform == _leftRayOrigin ||
                 colliderTransform.IsChildOf(_leftRayOrigin) ||
                 _leftRayOrigin.IsChildOf(colliderTransform)))
            {
                return true;
            }

            return false;
        }

        private void EmitPointerTelemetry(PointerShotRecord shot)
        {
            if (!_emitPointerTelemetry)
            {
                return;
            }

            var telemetry = PointerTelemetryService.Instance;
            if (telemetry == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "inputHand", _lastActivationHand },
                { "inputSource", "QUEST_POINTER" },
                { "inputControl", _lastActivationControl },
                { "inputValue", _lastActivationValue.ToString("F3", CultureInfo.InvariantCulture) },
                { "hitAnyCollider", shot.HitAnyCollider },
                { "hitInteractiveTarget", shot.HitInteractiveTarget },
                { "hasHoverFeedback", shot.HasHoverFeedback },
                { "hoverIsValidTarget", shot.HoverIsValidTarget },
                { "hitObjectName", shot.HitObjectName ?? string.Empty },
                { "hitDistanceMeters", shot.HitDistance.ToString("F3", CultureInfo.InvariantCulture) },
                { "indicatorColor", ColorToHex(shot.IndicatorColor) },
                { "gameId", ResolveActiveGameId() },
                { "sourceComponent", nameof(QuestPointerClickInteractor) },
            };

            telemetry.RecordShot(payload);

            if (_logPointerTelemetry)
            {
                Debug.Log(
                    $"[QuestPointer][Telemetry] hand={_lastActivationHand} control={_lastActivationControl} " +
                    $"hitAny={shot.HitAnyCollider} hitInteractive={shot.HitInteractiveTarget} valid={shot.HoverIsValidTarget}");
            }
        }

        private void EmitToolGripTelemetry(bool isPressed)
        {
            if (!_emitToolTelemetry)
            {
                return;
            }

            ResolveToolTelemetryDependencies();
            if (_toolGripTracker == null)
            {
                return;
            }

            _toolGripTracker.RecordGripState(new ToolGripTracker.ToolGripSample
            {
                toolId = ResolveToolId(),
                gameId = ResolveActiveGameId(),
                inputHand = _lastActivationHand,
                inputSource = "QUEST_POINTER",
                inputControl = _lastActivationControl,
                sourceComponent = nameof(QuestPointerClickInteractor),
                inputValue = _lastActivationValue,
                isPressed = isPressed,
                realtimeSinceStartup = Time.realtimeSinceStartup,
            });
        }

        private void EmitToolImpactTelemetry(PointerShotRecord shot)
        {
            if (!_emitToolTelemetry)
            {
                return;
            }

            ResolveToolTelemetryDependencies();
            if (_toolImpactProbe == null)
            {
                return;
            }

            var targetId = shot.ValidationZone == null
                ? shot.HitObjectName
                : shot.ValidationZone.TargetId;
            var targetName = shot.ValidationZone == null
                ? shot.HitObjectName
                : shot.ValidationZone.gameObject.name;

            _toolImpactProbe.RecordImpact(new ToolImpactProbe.ToolImpactSample
            {
                toolId = ResolveToolId(),
                gameId = ResolveActiveGameId(),
                inputHand = _lastActivationHand,
                inputSource = "QUEST_POINTER",
                inputControl = _lastActivationControl,
                sourceComponent = nameof(QuestPointerClickInteractor),
                targetId = targetId,
                targetName = targetName,
                inputValue = _lastActivationValue,
                impactForce = _lastActivationValue,
                hitDistanceMeters = shot.HitDistance,
                hitAnyCollider = shot.HitAnyCollider,
                hitInteractiveTarget = shot.HitInteractiveTarget,
                pointerSuggestedTargetValid = shot.HasHoverFeedback
                    ? shot.HoverIsValidTarget
                    : (bool?)null,
                hitCollider = shot.HitCollider,
                targetValidationZone = shot.ValidationZone,
                impactPointWorld = shot.HitPoint,
                impactNormalWorld = shot.HitNormal,
            });
        }

        private void UpdateLaserVisual()
        {
            if (!_showLaser)
            {
                SetLaserActive(_leftLaser, false);
                SetLaserActive(_rightLaser, false);

                return;
            }

            EnsureLaserRenderers();
            var activeHand = ResolveActiveHandToken();
            if (_showDualHandLasers)
            {
                UpdateSingleLaserVisual("LEFT", _leftLaser, _leftLaserFallbackColor, activeHand);
                UpdateSingleLaserVisual("RIGHT", _rightLaser, _rightLaserFallbackColor, activeHand);
                return;
            }

            if (string.Equals(activeHand, "LEFT", System.StringComparison.OrdinalIgnoreCase))
            {
                UpdateSingleLaserVisual("LEFT", _leftLaser, _leftLaserFallbackColor, activeHand);
                SetLaserActive(_rightLaser, false);
                return;
            }

            UpdateSingleLaserVisual("RIGHT", _rightLaser, _rightLaserFallbackColor, activeHand);
            SetLaserActive(_leftLaser, false);
        }

        private void UpdateSingleLaserVisual(
            string handToken,
            LaserVisual laser,
            Color idleColor,
            string activeHandToken)
        {
            if (laser == null || laser.line == null)
            {
                return;
            }

            if (!TryBuildRayForHand(
                    handToken,
                    allowFallbackToOtherHand: false,
                    out var ray))
            {
                SetLaserActive(laser, false);
                return;
            }

            var hasHit = TryRaycastFromRay(ray, out var hit);
            if (!hasHit && !_showLaserWhenNoHit)
            {
                SetLaserActive(laser, false);
                return;
            }

            var fallbackColor = idleColor;
            var isActiveHand = string.Equals(
                handToken,
                activeHandToken,
                System.StringComparison.OrdinalIgnoreCase);
            if (isActiveHand)
            {
                fallbackColor = ResolveSessionIndicatorColor();
            }

            var color = ResolveIndicatorColorForRay(hasHit, hit, fallbackColor);
            ApplyLaserStyle(laser.line, laser.material, color);

            var start = ray.origin;
            var maxDistance = Mathf.Max(0.1f, _maxDistance);
            var end = hasHit
                ? hit.point
                : ray.origin + (ray.direction * maxDistance);

            laser.line.enabled = true;
            laser.line.SetPosition(0, start);
            laser.line.SetPosition(1, end);
        }

        private void SetLaserActive(LaserVisual laser, bool isActive)
        {
            if (laser?.line == null)
            {
                return;
            }

            laser.line.enabled = isActive;
        }

        private void UpdateWandVisual()
        {
            if (!_showWand)
            {
                SetWandActive(_leftWand, false);
                SetWandActive(_rightWand, false);

                return;
            }

            EnsureWandVisuals();

            var hasHit = TryRaycastFromPointer(out _, out var hit);
            var activeColor = ResolveIndicatorColor(hasHit, hit);
            var activeHand = ResolveActiveHandToken();

            if (_showDualHandWands)
            {
                var leftColor = string.Equals(activeHand, "LEFT", System.StringComparison.OrdinalIgnoreCase)
                    ? activeColor
                    : _leftWandFallbackColor;
                var rightColor = string.Equals(activeHand, "RIGHT", System.StringComparison.OrdinalIgnoreCase)
                    ? activeColor
                    : _rightWandFallbackColor;

                UpdateSingleWandVisual("LEFT", _leftWand, leftColor);
                UpdateSingleWandVisual("RIGHT", _rightWand, rightColor);
                return;
            }

            if (string.Equals(activeHand, "LEFT", System.StringComparison.OrdinalIgnoreCase))
            {
                UpdateSingleWandVisual("LEFT", _leftWand, activeColor);
                SetWandActive(_rightWand, false);
                return;
            }

            UpdateSingleWandVisual("RIGHT", _rightWand, activeColor);
            SetWandActive(_leftWand, false);
        }

        private void UpdateSingleWandVisual(string handToken, WandVisual wand, Color color)
        {
            if (wand == null)
            {
                return;
            }

            if (!TryBuildRayForHand(
                    handToken,
                    allowFallbackToOtherHand: false,
                    out var ray))
            {
                SetWandActive(wand, false);
                return;
            }

            if (wand.body == null || wand.tip == null)
            {
                return;
            }

            var direction = ray.direction.sqrMagnitude > 0f ? ray.direction.normalized : Vector3.forward;
            var length = Mathf.Max(0.05f, _wandLength);
            var radius = Mathf.Max(0.001f, _wandRadius);
            var tipRadius = Mathf.Max(radius * 1.1f, _wandTipRadius);

            SetWandActive(wand, true);
            wand.body.position = ray.origin + (direction * (length * 0.5f));
            wand.body.rotation = Quaternion.FromToRotation(Vector3.up, direction);
            wand.body.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            wand.tip.position = ray.origin + (direction * length);
            wand.tip.rotation = Quaternion.identity;
            wand.tip.localScale = Vector3.one * tipRadius;

            if (wand.bodyMaterial != null)
            {
                wand.bodyMaterial.color = color;
            }

            if (wand.tipMaterial != null)
            {
                wand.tipMaterial.color = color;
            }
        }

        private void SetWandActive(WandVisual wand, bool isActive)
        {
            if (wand == null)
            {
                return;
            }

            if (wand.body != null)
            {
                wand.body.gameObject.SetActive(isActive);
            }

            if (wand.tip != null)
            {
                wand.tip.gameObject.SetActive(isActive);
            }
        }

        private void EnsureLaserRenderers()
        {
            EnsureSingleLaserRenderer(_leftLaser, "Left", _leftLaserFallbackColor);
            EnsureSingleLaserRenderer(_rightLaser, "Right", _rightLaserFallbackColor);
        }

        private void EnsureSingleLaserRenderer(LaserVisual laser, string handSuffix, Color fallbackColor)
        {
            if (laser == null || laser.line != null)
            {
                return;
            }

            var safeSuffix = string.IsNullOrWhiteSpace(handSuffix) ? "Unknown" : handSuffix.Trim();
            var host = new GameObject("QuestPointerLaser" + safeSuffix);
            host.transform.SetParent(transform, worldPositionStays: false);

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.enabled = false;

            Material material = null;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader != null)
            {
                material = new Material(shader)
                {
                    name = "QuestPointerLaserMaterial" + safeSuffix,
                    color = fallbackColor
                };
                line.material = material;
            }

            laser.line = line;
            laser.material = material;
            ApplyLaserStyle(line, material, fallbackColor);
        }

        private void EnsureWandVisuals()
        {
            EnsureSingleWandVisual(_leftWand, "Left", _leftWandFallbackColor);
            EnsureSingleWandVisual(_rightWand, "Right", _rightWandFallbackColor);
        }

        private void EnsureSingleWandVisual(WandVisual wand, string handSuffix, Color fallbackColor)
        {
            if (wand == null || (wand.body != null && wand.tip != null))
            {
                return;
            }

            var safeSuffix = string.IsNullOrWhiteSpace(handSuffix) ? "Unknown" : handSuffix.Trim();
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "QuestPointerWandBody" + safeSuffix;
            body.transform.SetParent(transform, worldPositionStays: true);
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
            {
                Destroy(bodyCollider);
            }

            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "QuestPointerWandTip" + safeSuffix;
            tip.transform.SetParent(transform, worldPositionStays: true);
            var tipCollider = tip.GetComponent<Collider>();
            if (tipCollider != null)
            {
                Destroy(tipCollider);
            }

            wand.body = body.transform;
            wand.tip = tip.transform;

            var bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                wand.bodyMaterial = new Material(bodyRenderer.sharedMaterial)
                {
                    name = "QuestPointerWandBodyMaterial" + safeSuffix,
                    color = fallbackColor
                };
                bodyRenderer.material = wand.bodyMaterial;
            }

            var tipRenderer = tip.GetComponent<Renderer>();
            if (tipRenderer != null)
            {
                wand.tipMaterial = new Material(tipRenderer.sharedMaterial)
                {
                    name = "QuestPointerWandTipMaterial" + safeSuffix,
                    color = fallbackColor
                };
                tipRenderer.material = wand.tipMaterial;
            }
        }

        private void DestroyWandVisual(WandVisual wand)
        {
            if (wand == null)
            {
                return;
            }

            if (wand.bodyMaterial != null)
            {
                Destroy(wand.bodyMaterial);
                wand.bodyMaterial = null;
            }

            if (wand.tipMaterial != null)
            {
                Destroy(wand.tipMaterial);
                wand.tipMaterial = null;
            }
        }

        private void DestroyLaserVisual(LaserVisual laser)
        {
            if (laser == null)
            {
                return;
            }

            if (laser.material != null)
            {
                Destroy(laser.material);
                laser.material = null;
            }
        }

        private void ApplyLaserStyle(LineRenderer line, Material material, Color color)
        {
            if (line == null)
            {
                return;
            }

            var width = Mathf.Max(0.0005f, _laserWidth);
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;

            if (material != null)
            {
                material.color = color;
            }
            else if (line.sharedMaterial != null)
            {
                line.sharedMaterial.color = color;
            }
        }

        private Color ResolveSessionIndicatorColor()
        {
            var fallback = _colorFromSessionIndicator ? _wandFallbackColor : _laserColor;
            if (!_colorFromSessionIndicator)
            {
                return fallback;
            }

            var service = PointerIndicatorService.Instance;
            if (service == null)
            {
                return fallback;
            }

            return service.ResolveIndicatorColor(fallback);
        }

        private Color ResolveIndicatorColor(bool hasHit, RaycastHit hit)
        {
            var sessionColor = ResolveSessionIndicatorColor();
            if (!hasHit || hit.collider == null)
            {
                return sessionColor;
            }

            if (TryResolveHoverFeedback(hit, out var feedback))
            {
                return feedback.IndicatorColor;
            }

            return sessionColor;
        }

        private static Color ResolveIndicatorColorForRay(bool hasHit, RaycastHit hit, Color fallbackColor)
        {
            if (!hasHit || hit.collider == null)
            {
                return fallbackColor;
            }

            return TryResolveHoverFeedback(hit, out var feedback)
                ? feedback.IndicatorColor
                : fallbackColor;
        }

        private static bool TryResolveHoverFeedback(RaycastHit hit, out PointerHoverFeedback feedback)
        {
            feedback = default;
            if (hit.collider == null)
            {
                return false;
            }

            var hoverTarget = hit.collider.GetComponentInParent<IPointerHoverFeedbackTarget>();
            return hoverTarget != null && hoverTarget.TryGetHoverFeedback(out feedback);
        }

        private static string ColorToHex(Color color)
        {
            var color32 = (Color32)color;
            return "#" + ColorUtility.ToHtmlStringRGBA(color32);
        }

        private string BuildPointerActivationSource()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "QUEST_POINTER|{0}|{1}|{2:F3}",
                string.IsNullOrWhiteSpace(_lastActivationHand) ? "UNKNOWN" : _lastActivationHand,
                string.IsNullOrWhiteSpace(_lastActivationControl) ? "UNKNOWN" : _lastActivationControl,
                _lastActivationValue);
        }

        private string ResolveActiveGameId()
        {
            if (_gameRuntimeService == null)
            {
                _gameRuntimeService = FindFirstObjectByType<GameRuntimeService>();
            }

            if (_gameRuntimeService == null || string.IsNullOrWhiteSpace(_gameRuntimeService.ActiveGameId))
            {
                return string.Empty;
            }

            return _gameRuntimeService.ActiveGameId.Trim();
        }

        private string ResolveToolId()
        {
            return string.IsNullOrWhiteSpace(_toolId)
                ? "quest_pointer_wand"
                : _toolId.Trim();
        }

        private void ResolveToolTelemetryDependencies()
        {
            if (_toolGripTracker == null)
            {
                _toolGripTracker = ToolGripTracker.Instance;
                if (_toolGripTracker == null)
                {
                    _toolGripTracker = FindFirstObjectByType<ToolGripTracker>();
                }
            }

            if (_toolImpactProbe == null)
            {
                _toolImpactProbe = ToolImpactProbe.Instance;
                if (_toolImpactProbe == null)
                {
                    _toolImpactProbe = FindFirstObjectByType<ToolImpactProbe>();
                }
            }
        }

        private void ReleaseToolGripIfNeeded()
        {
            if (!_emitToolTelemetry)
            {
                return;
            }

            ResolveToolTelemetryDependencies();
            _toolGripTracker?.ForceRelease(nameof(QuestPointerClickInteractor));
        }

        private Ray BuildPointerRay()
        {
            if (_switchRayOriginToPressedHand &&
                TryBuildRayForHand(_lastActivationHand, allowFallbackToOtherHand: false, out var pressedHandRay))
            {
                return pressedHandRay;
            }

            if (_preferLeftRayOrigin)
            {
                if (TryBuildRayForHand("LEFT", allowFallbackToOtherHand: true, out var leftRay))
                {
                    return leftRay;
                }

                if (TryBuildRayForHand("RIGHT", allowFallbackToOtherHand: true, out var rightRay))
                {
                    return rightRay;
                }
            }
            else
            {
                if (TryBuildRayForHand("RIGHT", allowFallbackToOtherHand: true, out var rightRay))
                {
                    return rightRay;
                }

                if (TryBuildRayForHand("LEFT", allowFallbackToOtherHand: true, out var leftRay))
                {
                    return leftRay;
                }
            }

            if (_rayCamera == null)
            {
                _rayCamera = Camera.main;
            }

            if (_rayCamera != null)
            {
                return new Ray(_rayCamera.transform.position, _rayCamera.transform.forward);
            }

            return new Ray(Vector3.zero, Vector3.forward);
        }

        private bool TryBuildRayForHand(string handToken, bool allowFallbackToOtherHand, out Ray ray)
        {
            var origin = ResolveRayOriginTransformForHand(handToken, allowFallbackToOtherHand);
            if (origin != null)
            {
                ray = BuildRayFromTransform(origin);
                return true;
            }

            if (_useCameraHandFallbackWhenOriginMissing &&
                TryBuildCameraFallbackRay(handToken, out ray))
            {
                return true;
            }

            ray = default;
            return false;
        }

        private bool TryBuildCameraFallbackRay(string handToken, out Ray ray)
        {
            if (_rayCamera == null)
            {
                _rayCamera = Camera.main;
            }

            if (_rayCamera == null)
            {
                ray = default;
                return false;
            }

            var safeHorizontal = Mathf.Max(0.02f, _cameraHandFallbackHorizontalOffset);
            var handSign = string.Equals(handToken, "LEFT", System.StringComparison.OrdinalIgnoreCase)
                ? -1f
                : 1f;
            var origin = _rayCamera.transform.position +
                (_rayCamera.transform.right * safeHorizontal * handSign) +
                (_rayCamera.transform.up * _cameraHandFallbackVerticalOffset) +
                (_rayCamera.transform.forward * _cameraHandFallbackForwardOffset);
            ray = new Ray(origin, _rayCamera.transform.forward);
            return true;
        }

        private static Ray BuildRayFromTransform(Transform origin)
        {
            if (origin == null)
            {
                return new Ray(Vector3.zero, Vector3.forward);
            }

            return new Ray(origin.position, origin.forward);
        }

        private string ResolveActiveHandToken()
        {
            if (string.Equals(_lastActivationHand, "LEFT", System.StringComparison.OrdinalIgnoreCase) &&
                _leftRayOrigin != null)
            {
                return "LEFT";
            }

            if (string.Equals(_lastActivationHand, "RIGHT", System.StringComparison.OrdinalIgnoreCase) &&
                _rightRayOrigin != null)
            {
                return "RIGHT";
            }

            if (_preferLeftRayOrigin)
            {
                if (_leftRayOrigin != null)
                {
                    return "LEFT";
                }

                if (_rightRayOrigin != null)
                {
                    return "RIGHT";
                }
            }
            else
            {
                if (_rightRayOrigin != null)
                {
                    return "RIGHT";
                }

                if (_leftRayOrigin != null)
                {
                    return "LEFT";
                }
            }

            return "RIGHT";
        }

        private Transform ResolveRayOriginTransformForHand(string handToken, bool allowFallbackToOtherHand)
        {
            var isLeftHand = string.Equals(handToken, "LEFT", System.StringComparison.OrdinalIgnoreCase);
            var isRightHand = string.Equals(handToken, "RIGHT", System.StringComparison.OrdinalIgnoreCase);

            if (isLeftHand && _leftRayOrigin != null)
            {
                return _leftRayOrigin;
            }

            if (isRightHand && _rightRayOrigin != null)
            {
                return _rightRayOrigin;
            }

            if (!allowFallbackToOtherHand)
            {
                return null;
            }

            if (isLeftHand)
            {
                return _rightRayOrigin;
            }

            if (isRightHand)
            {
                return _leftRayOrigin;
            }

            if (_preferLeftRayOrigin)
            {
                if (_leftRayOrigin != null)
                {
                    return _leftRayOrigin;
                }

                return _rightRayOrigin;
            }

            if (_rightRayOrigin != null)
            {
                return _rightRayOrigin;
            }

            return _leftRayOrigin;
        }

        private void EnsureDistinctRayOrigins()
        {
            if (_leftRayOrigin == null ||
                _rightRayOrigin == null ||
                _leftRayOrigin != _rightRayOrigin)
            {
                return;
            }

            var preferredLeft = FindRayOriginCandidate(isRight: false, excludedCandidate: _rightRayOrigin);
            if (preferredLeft != null && preferredLeft != _rightRayOrigin)
            {
                _leftRayOrigin = preferredLeft;
                return;
            }

            var preferredRight = FindRayOriginCandidate(isRight: true, excludedCandidate: _leftRayOrigin);
            if (preferredRight != null && preferredRight != _leftRayOrigin)
            {
                _rightRayOrigin = preferredRight;
            }
        }

        private bool IsPointerSuppressedForActiveGame()
        {
            if (!_hidePointerVisualsInBilateralMarkers)
            {
                return false;
            }

            var activeGameId = ResolveActiveGameId();
            if (string.IsNullOrWhiteSpace(activeGameId) || string.IsNullOrWhiteSpace(_bilateralMarkersGameId))
            {
                return false;
            }

            return string.Equals(
                activeGameId.Trim(),
                _bilateralMarkersGameId.Trim(),
                System.StringComparison.OrdinalIgnoreCase);
        }

        private void HidePointerVisuals()
        {
            SetLaserActive(_leftLaser, false);
            SetLaserActive(_rightLaser, false);

            SetWandActive(_leftWand, false);
            SetWandActive(_rightWand, false);
        }

        private static Transform FindRayOriginCandidate(bool isRight, Transform excludedCandidate = null)
        {
            var candidates = isRight ? RightRayOriginCandidates : LeftRayOriginCandidates;
            for (var i = 0; i < candidates.Length; i++)
            {
                var go = GameObject.Find(candidates[i]);
                if (go != null && go.transform != excludedCandidate)
                {
                    return go.transform;
                }
            }

            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var handToken = isRight ? "right" : "left";
            Transform bestCandidate = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate == excludedCandidate)
                {
                    continue;
                }

                var name = candidate.name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var normalized = name.ToLowerInvariant();
                if (normalized.Contains(handToken) &&
                    (normalized.Contains("hand") || normalized.Contains("controller") || normalized.Contains("anchor")))
                {
                    var score = ScoreRayOriginCandidate(candidate, handToken);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCandidate = candidate;
                    }
                }
            }

            return bestCandidate;
        }

        private static int ScoreRayOriginCandidate(Transform candidate, string handToken)
        {
            if (candidate == null)
            {
                return int.MinValue;
            }

            var normalizedName = (candidate.name ?? string.Empty).ToLowerInvariant();
            var score = 0;
            if (normalizedName.Contains(handToken))
            {
                score += 60;
            }

            if (normalizedName.Contains("inhand"))
            {
                score += 120;
            }

            if (normalizedName.Contains("oncontroller"))
            {
                score += 100;
            }

            if (normalizedName.Contains("touchcontroller"))
            {
                score += 90;
            }

            if (normalizedName.Contains("controller"))
            {
                score += 75;
            }

            if (normalizedName.Contains("handanchor"))
            {
                score += 50;
            }

            if (normalizedName.Contains("anchor"))
            {
                score += 25;
            }

            if (normalizedName.Contains("detached"))
            {
                score -= 80;
            }

            var path = BuildTransformPath(candidate);
            if (path.Contains("trackingspace"))
            {
                score += 20;
            }

            if (path.Contains("ovrcamerarig"))
            {
                score += 20;
            }

            return score;
        }

        private static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var pathParts = new List<string>(8);
            var current = transform;
            while (current != null)
            {
                pathParts.Add(current.name ?? string.Empty);
                current = current.parent;
            }

            pathParts.Reverse();
            return string.Join("/", pathParts).ToLowerInvariant();
        }
    }

    public static class QuestPointerClickInteractorBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInteractor()
        {
            var existing = Object.FindFirstObjectByType<QuestPointerClickInteractor>();
            if (existing != null)
            {
                return;
            }

            var host = new GameObject("QuestPointerClickInteractor");
            host.AddComponent<QuestPointerClickInteractor>();
        }
    }
}
