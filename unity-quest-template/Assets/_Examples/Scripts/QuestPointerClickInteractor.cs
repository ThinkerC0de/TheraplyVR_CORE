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
            "OVRCameraRig/TrackingSpace/RightHandAnchor",
            "XR Origin/Camera Offset/Right Controller",
            "XR Origin/Right Controller",
            "RightHand Controller",
            "Right Controller",
        };

        private static readonly string[] LeftRayOriginCandidates =
        {
            "OVRCameraRig/TrackingSpace/LeftHandAnchor",
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

        [Header("Raycast")]
        [SerializeField] private float _maxDistance = 20f;
        [SerializeField] private LayerMask _hitMask = ~0;
        [SerializeField] private bool _includeTriggerColliders = true;
        [SerializeField] private bool _ignorePointerRigColliders = true;

        [Header("Laser Pointer")]
        [SerializeField] private bool _showLaser = true;
        [SerializeField] private bool _showLaserWhenNoHit = true;
        [SerializeField] private float _laserWidth = 0.005f;
        [SerializeField] private Color _laserColor = new Color(0.2f, 0.9f, 1f, 0.9f);

        [Header("Wand Indicator")]
        [SerializeField] private bool _showWand = true;
        [SerializeField] private float _wandLength = 0.24f;
        [SerializeField] private float _wandRadius = 0.008f;
        [SerializeField] private float _wandTipRadius = 0.016f;
        [SerializeField] private bool _hidePointerVisualsInBilateralMarkers = true;
        [SerializeField] private string _bilateralMarkersGameId = "bilateral_markers";
        [SerializeField] private bool _colorFromSessionIndicator = true;
        [SerializeField] private Color _wandFallbackColor = new Color(0.2f, 0.9f, 1f, 0.95f);

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
        private LineRenderer _laserLine;
        private Material _laserMaterial;
        private Transform _wandBody;
        private Transform _wandTip;
        private Material _wandBodyMaterial;
        private Material _wandTipMaterial;
        private string _lastActivationHand = "UNKNOWN";
        private string _lastActivationControl = "UNKNOWN";
        private float _lastActivationValue = 0f;

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
            EnsureLaserRenderer();
            EnsureWandVisual();
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

            if (_laserMaterial != null)
            {
                Destroy(_laserMaterial);
            }

            if (_wandBodyMaterial != null)
            {
                Destroy(_wandBodyMaterial);
            }

            if (_wandTipMaterial != null)
            {
                Destroy(_wandTipMaterial);
            }
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

            if (_rightRayOrigin == null)
            {
                _rightRayOrigin = FindRayOriginCandidate(true);
            }

            if (_leftRayOrigin == null)
            {
                _leftRayOrigin = FindRayOriginCandidate(false);
            }

            if (_rayCamera == null)
            {
                _rayCamera = Camera.main;
            }
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
                if (_laserLine != null)
                {
                    _laserLine.enabled = false;
                }

                return;
            }

            EnsureLaserRenderer();
            if (_laserLine == null)
            {
                return;
            }

            var hasHit = TryRaycastFromPointer(out var ray, out var hit);
            var color = ResolveIndicatorColor(hasHit, hit);
            ApplyLaserStyle(color);
            if (!hasHit && !_showLaserWhenNoHit)
            {
                _laserLine.enabled = false;
                return;
            }

            var start = ray.origin;
            var maxDistance = Mathf.Max(0.1f, _maxDistance);
            var end = hasHit
                ? hit.point
                : ray.origin + (ray.direction * maxDistance);

            _laserLine.enabled = true;
            _laserLine.SetPosition(0, start);
            _laserLine.SetPosition(1, end);
        }

        private void UpdateWandVisual()
        {
            if (!_showWand)
            {
                if (_wandBody != null)
                {
                    _wandBody.gameObject.SetActive(false);
                }

                if (_wandTip != null)
                {
                    _wandTip.gameObject.SetActive(false);
                }

                return;
            }

            EnsureWandVisual();
            if (_wandBody == null || _wandTip == null)
            {
                return;
            }

            var hasHit = TryRaycastFromPointer(out var ray, out var hit);
            var direction = ray.direction.sqrMagnitude > 0f ? ray.direction.normalized : Vector3.forward;
            var length = Mathf.Max(0.05f, _wandLength);
            var radius = Mathf.Max(0.001f, _wandRadius);
            var tipRadius = Mathf.Max(radius * 1.1f, _wandTipRadius);
            var color = ResolveIndicatorColor(hasHit, hit);

            _wandBody.gameObject.SetActive(true);
            _wandTip.gameObject.SetActive(true);

            _wandBody.position = ray.origin + (direction * (length * 0.5f));
            _wandBody.rotation = Quaternion.FromToRotation(Vector3.up, direction);
            _wandBody.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);

            _wandTip.position = ray.origin + (direction * length);
            _wandTip.rotation = Quaternion.identity;
            _wandTip.localScale = Vector3.one * tipRadius;

            if (_wandBodyMaterial != null)
            {
                _wandBodyMaterial.color = color;
            }

            if (_wandTipMaterial != null)
            {
                _wandTipMaterial.color = color;
            }
        }

        private void EnsureLaserRenderer()
        {
            if (_laserLine != null)
            {
                return;
            }

            _laserLine = GetComponent<LineRenderer>();
            if (_laserLine == null)
            {
                _laserLine = gameObject.AddComponent<LineRenderer>();
            }

            _laserLine.useWorldSpace = true;
            _laserLine.positionCount = 2;
            _laserLine.numCornerVertices = 2;
            _laserLine.numCapVertices = 2;
            _laserLine.enabled = false;

            if (_laserLine.sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }

                if (shader != null)
                {
                    _laserMaterial = new Material(shader)
                    {
                        name = "QuestPointerLaserMaterial"
                    };
                    _laserLine.material = _laserMaterial;
                }
            }

            ApplyLaserStyle(ResolveSessionIndicatorColor());
        }

        private void EnsureWandVisual()
        {
            if (_wandBody != null && _wandTip != null)
            {
                return;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "QuestPointerWandBody";
            body.transform.SetParent(transform, worldPositionStays: true);
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
            {
                Destroy(bodyCollider);
            }

            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "QuestPointerWandTip";
            tip.transform.SetParent(transform, worldPositionStays: true);
            var tipCollider = tip.GetComponent<Collider>();
            if (tipCollider != null)
            {
                Destroy(tipCollider);
            }

            _wandBody = body.transform;
            _wandTip = tip.transform;

            var bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                _wandBodyMaterial = new Material(bodyRenderer.sharedMaterial)
                {
                    name = "QuestPointerWandBodyMaterial"
                };
                bodyRenderer.material = _wandBodyMaterial;
            }

            var tipRenderer = tip.GetComponent<Renderer>();
            if (tipRenderer != null)
            {
                _wandTipMaterial = new Material(tipRenderer.sharedMaterial)
                {
                    name = "QuestPointerWandTipMaterial"
                };
                tipRenderer.material = _wandTipMaterial;
            }
        }

        private void ApplyLaserStyle(Color color)
        {
            if (_laserLine == null)
            {
                return;
            }

            var width = Mathf.Max(0.0005f, _laserWidth);
            _laserLine.startWidth = width;
            _laserLine.endWidth = width;
            _laserLine.startColor = color;
            _laserLine.endColor = color;

            if (_laserMaterial != null)
            {
                _laserMaterial.color = color;
            }
            else if (_laserLine.sharedMaterial != null)
            {
                _laserLine.sharedMaterial.color = color;
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
            var origin = ResolveRayOriginTransform();
            if (origin != null)
            {
                return new Ray(origin.position, origin.forward);
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

        private Transform ResolveRayOriginTransform()
        {
            if (_switchRayOriginToPressedHand)
            {
                if (string.Equals(_lastActivationHand, "LEFT", System.StringComparison.OrdinalIgnoreCase) &&
                    _leftRayOrigin != null)
                {
                    return _leftRayOrigin;
                }

                if (string.Equals(_lastActivationHand, "RIGHT", System.StringComparison.OrdinalIgnoreCase) &&
                    _rightRayOrigin != null)
                {
                    return _rightRayOrigin;
                }
            }

            if (_preferLeftRayOrigin)
            {
                if (_leftRayOrigin != null)
                {
                    return _leftRayOrigin;
                }

                if (_rightRayOrigin != null)
                {
                    return _rightRayOrigin;
                }
            }
            else
            {
                if (_rightRayOrigin != null)
                {
                    return _rightRayOrigin;
                }

                if (_leftRayOrigin != null)
                {
                    return _leftRayOrigin;
                }
            }

            return null;
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
            if (_laserLine != null)
            {
                _laserLine.enabled = false;
            }

            if (_wandBody != null)
            {
                _wandBody.gameObject.SetActive(false);
            }

            if (_wandTip != null)
            {
                _wandTip.gameObject.SetActive(false);
            }
        }

        private static Transform FindRayOriginCandidate(bool isRight)
        {
            var candidates = isRight ? RightRayOriginCandidates : LeftRayOriginCandidates;
            for (var i = 0; i < candidates.Length; i++)
            {
                var go = GameObject.Find(candidates[i]);
                if (go != null)
                {
                    return go.transform;
                }
            }

            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var handToken = isRight ? "right" : "left";
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var name = allTransforms[i].name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var normalized = name.ToLowerInvariant();
                if (normalized.Contains(handToken) &&
                    (normalized.Contains("hand") || normalized.Contains("controller") || normalized.Contains("anchor")))
                {
                    return allTransforms[i];
                }
            }

            return null;
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
