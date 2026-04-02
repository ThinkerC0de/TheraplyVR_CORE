using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
        [SerializeField] private bool _compressTraceWithGzip = true;
        [SerializeField] private bool _persistDebugTraceAsNdjson = false;
        [SerializeField] private bool _includeTracePayloadInTraceRefEvent = true;
        [SerializeField] [Range(16384, 900000)] private int _maxInlineTracePayloadBytes = 512000;
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
            var persistedTraceBytes = Array.Empty<byte>();
            var frameCount = _samples.Count;

            if (_persistTraceToDisk && frameCount > 0)
            {
                if (TryPersistTrace(
                    out tracePath,
                    out checksum,
                    out encoding,
                    out persistedTraceBytes))
                {
                    // encoding is assigned by the serializer output path.
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
                    { "format", "vrl" },
                    { "checksum", NormalizeOrFallback(checksum, string.Empty) },
                    { "frameCount", frameCount },
                    { "tracePath", NormalizeOrFallback(tracePath, string.Empty) },
                    { "trace_ref", NormalizeOrFallback(_traceId, string.Empty) },
                    { "reasonCode", NormalizeOrFallback(reasonCode, "TRACE_READY") },
                    { "payloadVersion", 1 },
                    { "monotonicSec", Time.realtimeSinceStartup },
                    { "actionOutcome", "OBSERVED" },
                };
                AppendInlinePayloadMetadata(payload, persistedTraceBytes, encoding);

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

        private void AppendInlinePayloadMetadata(
            Dictionary<string, object> payload,
            byte[] persistedTraceBytes,
            string encoding)
        {
            if (payload == null)
            {
                return;
            }

            var safeMaxInlineBytes = Mathf.Clamp(_maxInlineTracePayloadBytes, 16384, 900000);
            payload["tracePayloadFormat"] = "VRL";
            payload["tracePayloadEncoding"] = NormalizeOrFallback(encoding, "none");
            payload["inlinePayloadEnabled"] = _includeTracePayloadInTraceRefEvent;
            payload["inlinePayloadMaxBytes"] = safeMaxInlineBytes;

            if (!_includeTracePayloadInTraceRefEvent)
            {
                payload["inlinePayloadStatus"] = "DISABLED";
                return;
            }

            if (persistedTraceBytes == null || persistedTraceBytes.Length <= 0)
            {
                payload["inlinePayloadStatus"] = "UNAVAILABLE";
                return;
            }

            payload["inlinePayloadBytes"] = persistedTraceBytes.Length;
            if (persistedTraceBytes.Length > safeMaxInlineBytes)
            {
                payload["inlinePayloadStatus"] = "SKIPPED_SIZE_LIMIT";
                return;
            }

            payload["tracePayloadBase64"] = Convert.ToBase64String(persistedTraceBytes);
            payload["inlinePayloadStatus"] = "INCLUDED";
        }

        private bool TryPersistTrace(
            out string tracePath,
            out string checksum,
            out string encoding,
            out byte[] persistedTraceBytes)
        {
            tracePath = string.Empty;
            checksum = string.Empty;
            encoding = "volatile";
            persistedTraceBytes = Array.Empty<byte>();

            try
            {
                var folder = string.IsNullOrWhiteSpace(_traceFolder)
                    ? "session_resilience/traces"
                    : _traceFolder.Trim();
                var fullFolderPath = Path.Combine(Application.persistentDataPath, folder);
                Directory.CreateDirectory(fullFolderPath);

                var safeSessionId = SanitizeFileToken(_activeSessionId, "session");
                var safeTraceId = SanitizeFileToken(_traceId, "trace");
                var fileExtension = _compressTraceWithGzip ? ".vrl.gz" : ".vrl";
                var fileName = safeSessionId + "_" + safeTraceId + fileExtension;
                tracePath = Path.Combine(fullFolderPath, fileName);

                var bytes = BuildTraceBinaryContent();
                if (_compressTraceWithGzip)
                {
                    bytes = CompressWithGzip(bytes);
                    encoding = "vrl_gzip";
                }
                else
                {
                    encoding = "vrl";
                }

                checksum = ComputeSha256Hex(bytes);
                File.WriteAllBytes(tracePath, bytes);
                persistedTraceBytes = bytes;

                if (_persistDebugTraceAsNdjson)
                {
                    var debugPath = Path.Combine(fullFolderPath, safeSessionId + "_" + safeTraceId + ".ndjson");
                    var debugContent = BuildTraceDebugContent();
                    File.WriteAllText(debugPath, debugContent, new UTF8Encoding(false));
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.Warning($"[MotionTraceRecorder] Failed to persist trace: {e.Message}");
                return false;
            }
        }

        private byte[] BuildTraceBinaryContent()
        {
            using (var stream = new MemoryStream(Mathf.Max(1024, _samples.Count * 48)))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write((byte)'V');
                writer.Write((byte)'R');
                writer.Write((byte)'L');
                writer.Write((byte)1); // format schema revision
                writer.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(_sampleRateHz), 1, 240));
                WriteString(writer, _activeSessionId);
                WriteString(writer, _activeGameId);
                WriteString(writer, _activeFlowId);
                WriteString(writer, _traceId);
                writer.Write(_samples.Count);

                for (var i = 0; i < _samples.Count; i++)
                {
                    WriteSample(writer, i, _samples[i]);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteSample(BinaryWriter writer, int index, MotionSample sample)
        {
            var presenceMask = 0;
            if (sample.head.hasData)
            {
                presenceMask |= 1;
            }

            if (sample.left.hasData)
            {
                presenceMask |= 1 << 1;
            }

            if (sample.right.hasData)
            {
                presenceMask |= 1 << 2;
            }

            writer.Write((byte)presenceMask);
            writer.Write(index);
            writer.Write(sample.monotonicSec);
            if (sample.head.hasData)
            {
                WritePose(writer, sample.head);
            }

            if (sample.left.hasData)
            {
                WritePose(writer, sample.left);
            }

            if (sample.right.hasData)
            {
                WritePose(writer, sample.right);
            }
        }

        private static void WritePose(BinaryWriter writer, PoseSample pose)
        {
            WriteHalf(writer, pose.position.x);
            WriteHalf(writer, pose.position.y);
            WriteHalf(writer, pose.position.z);
            WriteHalf(writer, pose.rotation.x);
            WriteHalf(writer, pose.rotation.y);
            WriteHalf(writer, pose.rotation.z);
            WriteHalf(writer, pose.rotation.w);
        }

        private static void WriteHalf(BinaryWriter writer, float value)
        {
            writer.Write(FloatToHalfBits(value));
        }

        private static ushort FloatToHalfBits(float value)
        {
            var bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            var sign = (bits >> 31) & 0x1u;
            var exponent = (bits >> 23) & 0xFFu;
            var mantissa = bits & 0x7FFFFFu;

            if (exponent == 255u)
            {
                var halfNaN = mantissa == 0u ? 0u : 0x200u;
                return (ushort)((sign << 15) | 0x7C00u | halfNaN);
            }

            var adjustedExp = (int)exponent - 127 + 15;
            if (adjustedExp >= 31)
            {
                return (ushort)((sign << 15) | 0x7C00u);
            }

            if (adjustedExp <= 0)
            {
                if (adjustedExp < -10)
                {
                    return (ushort)(sign << 15);
                }

                mantissa |= 0x800000u;
                var shift = 14 - adjustedExp;
                var halfMantissa = mantissa >> shift;
                var roundingBit = (mantissa >> (shift - 1)) & 1u;
                halfMantissa += roundingBit;
                return (ushort)((sign << 15) | (halfMantissa & 0x3FFu));
            }

            var roundedMantissa = mantissa + 0x1000u;
            if ((roundedMantissa & 0x800000u) != 0u)
            {
                roundedMantissa = 0u;
                adjustedExp += 1;
                if (adjustedExp >= 31)
                {
                    return (ushort)((sign << 15) | 0x7C00u);
                }
            }

            return (ushort)((sign << 15) | ((uint)adjustedExp << 10) | (roundedMantissa >> 13));
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var normalized = NormalizeOrFallback(value, string.Empty);
            var bytes = Encoding.UTF8.GetBytes(normalized);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static byte[] CompressWithGzip(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 0)
            {
                return Array.Empty<byte>();
            }

            using (var output = new MemoryStream(bytes.Length))
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    gzip.Write(bytes, 0, bytes.Length);
                }

                return output.ToArray();
            }
        }

        private string BuildTraceDebugContent()
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
