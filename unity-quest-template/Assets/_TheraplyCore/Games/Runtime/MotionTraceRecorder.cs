using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using TheraplyCore.Interactions;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Optional compact motion trace recorder.
    /// Persists sampled transforms and emits trace_ref linkage into canonical telemetry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionTraceRecorder : MonoBehaviour
    {
        private struct PoseSample
        {
            public bool hasData;
            public Vector3 position;
            public Quaternion rotation;
        }

        private struct MotionSample
        {
            public float monotonicSec;
            public PoseSample head;
            public PoseSample left;
            public PoseSample right;
        }

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        [Header("Tracked Transforms")]
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _leftHand;
        [SerializeField] private Transform _rightHand;
        [SerializeField] private bool _autoResolveRigTransforms = true;

        [Header("Sampling")]
        [SerializeField] [Range(5f, 120f)] private float _sampleRateHz = 20f;
        [SerializeField] [Range(128, 120000)] private int _maxSamples = 24000;
        [SerializeField] private bool _persistTraceToDisk = true;
        [SerializeField] private string _traceFolder = "session_resilience/traces";
        [SerializeField] private bool _emitTraceRefEvent = true;
        [SerializeField] private bool _logLifecycle;

        private readonly List<MotionSample> _samples = new List<MotionSample>(2048);
        private string _traceId = string.Empty;
        private string _activeGameId = string.Empty;
        private string _activeFlowId = string.Empty;
        private string _activeSessionId = string.Empty;
        private bool _isRecording;
        private float _nextSampleAtSec;

        public bool IsRecording => _isRecording;
        public string ActiveTraceId => _traceId;
        public int SampleCount => _samples.Count;

        public void ConfigureTrackedTransforms(
            Transform head,
            Transform leftHand,
            Transform rightHand,
            bool disableAutoResolveWhenProvided = true)
        {
            if (head != null)
            {
                _head = head;
            }

            if (leftHand != null)
            {
                _leftHand = leftHand;
            }

            if (rightHand != null)
            {
                _rightHand = rightHand;
            }

            if (disableAutoResolveWhenProvided &&
                (head != null || leftHand != null || rightHand != null))
            {
                _autoResolveRigTransforms = false;
            }
        }

        private void Awake()
        {
            ResolveDependencies();
            if (_autoResolveRigTransforms)
            {
                ResolveDefaultTransforms();
            }
        }

        private void Update()
        {
            if (!_isRecording)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            var sampleInterval = Mathf.Max(1f / 120f, 1f / Mathf.Max(1f, _sampleRateHz));
            while (now >= _nextSampleAtSec)
            {
                CaptureSample(_nextSampleAtSec);
                _nextSampleAtSec += sampleInterval;
            }
        }

        public void BeginTrace(string gameId, string flowId, string sessionId)
        {
            ResolveDependencies();
            if (_autoResolveRigTransforms)
            {
                ResolveDefaultTransforms();
            }

            _samples.Clear();
            _traceId = Guid.NewGuid().ToString("N");
            _activeGameId = NormalizeOrFallback(gameId, "session_flow");
            _activeFlowId = NormalizeOrFallback(flowId, "session_flow");
            _activeSessionId = NormalizeOrFallback(sessionId, "unknown_session");
            _isRecording = true;
            _nextSampleAtSec = Time.realtimeSinceStartup;

            if (_logLifecycle)
            {
                Logger.Info(
                    "[MotionTraceRecorder] trace started traceId=" + _traceId +
                    " sessionId=" + _activeSessionId +
                    " flowId=" + _activeFlowId);
            }
        }

        public bool StopAndPublish(string reasonCode)
        {
            if (!_isRecording && _samples.Count <= 0)
            {
                return false;
            }

            _isRecording = false;

            var tracePath = string.Empty;
            var encoding = "none";
            var checksum = string.Empty;
            var frameCount = _samples.Count;

            if (_persistTraceToDisk && frameCount > 0)
            {
                if (TryPersistTrace(out tracePath, out checksum))
                {
                    encoding = "ndjson";
                }
                else
                {
                    encoding = "volatile";
                }
            }

            if (_emitTraceRefEvent && _interactionEventBridge != null)
            {
                var payload = new Dictionary<string, object>
                {
                    { "flowId", NormalizeOrFallback(_activeFlowId, "session_flow") },
                    { "stepId", string.Empty },
                    { "nodeId", string.Empty },
                    { "traceId", NormalizeOrFallback(_traceId, Guid.NewGuid().ToString("N")) },
                    { "traceType", "motion_trace" },
                    { "encoding", encoding },
                    { "checksum", NormalizeOrFallback(checksum, string.Empty) },
                    { "frameCount", frameCount },
                    { "tracePath", NormalizeOrFallback(tracePath, string.Empty) },
                    { "trace_ref", NormalizeOrFallback(_traceId, string.Empty) },
                    { "reasonCode", NormalizeOrFallback(reasonCode, "TRACE_READY") },
                    { "payloadVersion", 1 },
                    { "monotonicSec", Time.realtimeSinceStartup },
                    { "actionOutcome", "OBSERVED" },
                };

                _interactionEventBridge.RecordGameplayEvent(
                    NormalizeOrFallback(_activeGameId, "session_flow"),
                    "trace_ref",
                    string.Empty,
                    payload,
                    nameof(MotionTraceRecorder));
            }

            if (_logLifecycle)
            {
                Logger.Info(
                    "[MotionTraceRecorder] trace published traceId=" + NormalizeOrFallback(_traceId, string.Empty) +
                    " frames=" + frameCount +
                    " encoding=" + encoding);
            }

            _samples.Clear();
            _traceId = string.Empty;
            return true;
        }

        public void CancelTrace()
        {
            _isRecording = false;
            _samples.Clear();
            _traceId = string.Empty;
        }

        private void ResolveDependencies()
        {
            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
                }
            }
        }

        private void ResolveDefaultTransforms()
        {
            if (_head == null)
            {
                var camera = Camera.main;
                if (camera != null)
                {
                    _head = camera.transform;
                }
            }
        }

        private void CaptureSample(float sampleTimeSec)
        {
            if (_samples.Count >= Mathf.Max(16, _maxSamples))
            {
                _samples.RemoveAt(0);
            }

            var sample = new MotionSample
            {
                monotonicSec = sampleTimeSec,
                head = CapturePose(_head),
                left = CapturePose(_leftHand),
                right = CapturePose(_rightHand),
            };
            _samples.Add(sample);
        }

        private static PoseSample CapturePose(Transform transform)
        {
            if (transform == null)
            {
                return default;
            }

            return new PoseSample
            {
                hasData = true,
                position = transform.position,
                rotation = transform.rotation,
            };
        }

        private bool TryPersistTrace(out string tracePath, out string checksum)
        {
            tracePath = string.Empty;
            checksum = string.Empty;

            try
            {
                var folder = string.IsNullOrWhiteSpace(_traceFolder)
                    ? "session_resilience/traces"
                    : _traceFolder.Trim();
                var fullFolderPath = Path.Combine(Application.persistentDataPath, folder);
                Directory.CreateDirectory(fullFolderPath);

                var safeSessionId = SanitizeFileToken(_activeSessionId, "session");
                var safeTraceId = SanitizeFileToken(_traceId, "trace");
                var fileName = safeSessionId + "_" + safeTraceId + ".ndjson";
                tracePath = Path.Combine(fullFolderPath, fileName);

                var content = BuildTraceContent();
                var bytes = Encoding.UTF8.GetBytes(content);
                checksum = ComputeSha256Hex(bytes);
                File.WriteAllText(tracePath, content, new UTF8Encoding(false));
                return true;
            }
            catch (Exception e)
            {
                Logger.Warning($"[MotionTraceRecorder] Failed to persist trace: {e.Message}");
                return false;
            }
        }

        private string BuildTraceContent()
        {
            var builder = new StringBuilder(Mathf.Max(256, _samples.Count * 96));
            for (var i = 0; i < _samples.Count; i++)
            {
                AppendSampleLine(builder, _samples[i]);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void AppendSampleLine(StringBuilder builder, MotionSample sample)
        {
            builder.Append("{\"t\":");
            AppendFloat(builder, sample.monotonicSec);
            builder.Append(",\"head\":");
            AppendPose(builder, sample.head);
            builder.Append(",\"left\":");
            AppendPose(builder, sample.left);
            builder.Append(",\"right\":");
            AppendPose(builder, sample.right);
            builder.Append('}');
        }

        private static void AppendPose(StringBuilder builder, PoseSample pose)
        {
            if (!pose.hasData)
            {
                builder.Append("null");
                return;
            }

            builder.Append("{\"p\":[");
            AppendFloat(builder, pose.position.x);
            builder.Append(',');
            AppendFloat(builder, pose.position.y);
            builder.Append(',');
            AppendFloat(builder, pose.position.z);
            builder.Append("],\"r\":[");
            AppendFloat(builder, pose.rotation.x);
            builder.Append(',');
            AppendFloat(builder, pose.rotation.y);
            builder.Append(',');
            AppendFloat(builder, pose.rotation.z);
            builder.Append(',');
            AppendFloat(builder, pose.rotation.w);
            builder.Append("]}");
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 0)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(bytes);
                var hashBuilder = new StringBuilder(hashBytes.Length * 2);
                for (var i = 0; i < hashBytes.Length; i++)
                {
                    hashBuilder.Append(hashBytes[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return hashBuilder.ToString();
            }
        }

        private static string SanitizeFileToken(string value, string fallback)
        {
            var normalized = NormalizeOrFallback(value, fallback);
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(normalized.Length);
            for (var i = 0; i < normalized.Length; i++)
            {
                var ch = normalized[i];
                var isInvalid = false;
                for (var j = 0; j < invalidChars.Length; j++)
                {
                    if (ch == invalidChars[j])
                    {
                        isInvalid = true;
                        break;
                    }
                }

                builder.Append(isInvalid ? '_' : ch);
            }

            var safe = builder.ToString();
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
