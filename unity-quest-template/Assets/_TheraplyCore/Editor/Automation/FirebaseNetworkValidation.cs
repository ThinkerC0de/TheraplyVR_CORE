using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TheraplyCore.Firebase;
using TheraplyCore.Games;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Editor.Automation
{
    public static class FirebaseNetworkValidation
    {
        private const string ValidationScenePath = "Assets/_Examples/Scenes/SessionResilienceTest.unity";
        private const string ValidationGameIdArgument = "validationGameId";
        private static Task<string> _activeValidationTask;
        private static int _completionHandled;

        public static void RunFirebaseNetworkValidation()
        {
            if (_activeValidationTask != null)
            {
                Debug.LogWarning("[FirebaseNetworkValidation] Validation already running.");
                return;
            }

            _completionHandled = 0;
            _activeValidationTask = RunValidationAsync();
            EditorApplication.update += PumpValidationTask;
            StartCompletionWatcher(_activeValidationTask);
        }

        private static void PumpValidationTask()
        {
            if (_activeValidationTask == null)
            {
                EditorApplication.update -= PumpValidationTask;
                return;
            }

            if (!_activeValidationTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PumpValidationTask;
            CompleteFromTask(_activeValidationTask);
        }

        private static void StartCompletionWatcher(Task<string> validationTask)
        {
            if (validationTask == null)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await validationTask.ConfigureAwait(false);
                }
                catch
                {
                    // Completion is handled by CompleteFromTask below.
                }

                CompleteFromTask(validationTask);
            });
        }

        private static void CompleteFromTask(Task<string> completedTask)
        {
            if (completedTask == null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _completionHandled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var summary = completedTask.GetAwaiter().GetResult();
                WriteCompletionLog($"[FirebaseNetworkValidation] PASS: {summary}", false);
                PersistValidationResult("PASS", summary);
                SafeExit(0);
            }
            catch (Exception exception)
            {
                var unwrapped = exception is AggregateException aggregate
                    ? aggregate.GetBaseException()
                    : exception;
                WriteCompletionLog($"[FirebaseNetworkValidation] FAIL: {unwrapped}", true);
                PersistValidationResult("FAIL", unwrapped.ToString());
                SafeExit(1);
            }
            finally
            {
                _activeValidationTask = null;
                EditorApplication.update -= PumpValidationTask;
            }
        }

        private static void WriteCompletionLog(string message, bool isError)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                if (isError)
                {
                    Debug.LogError(message);
                }
                else
                {
                    Debug.Log(message);
                }
            }
            catch
            {
                // Ignore Unity logging failures from background watcher thread.
            }

            Console.WriteLine(message);
        }

        private static void SafeExit(int exitCode)
        {
            try
            {
                EditorApplication.Exit(exitCode);
            }
            catch
            {
                // Ignore and force shutdown fallback below.
            }

            Task.Run(async () =>
            {
                await Task.Delay(1500).ConfigureAwait(false);
                Environment.Exit(exitCode);
            });
        }

        private static async Task<string> RunValidationAsync()
        {
            EditorSceneManager.OpenScene(ValidationScenePath, OpenSceneMode.Single);

            var runtime = UnityEngine.Object.FindFirstObjectByType<GameRuntimeService>();
            var registry = UnityEngine.Object.FindFirstObjectByType<GameRegistryService>();
            var sessionContext = UnityEngine.Object.FindFirstObjectByType<GameSessionContext>();
            var firebase = UnityEngine.Object.FindFirstObjectByType<FirebaseDataService>();

            if (runtime == null || registry == null || sessionContext == null || firebase == null)
            {
                throw new InvalidOperationException(
                    "Validation scene is missing one or more required components (runtime/registry/session/firebase).");
            }

            var port = ReserveTcpPort();
            using (var backend = new LocalValidationBackend(port))
            {
                await backend.StartAsync();

                ConfigureFirebase(firebase, port);
                InvokeNonPublic(firebase, "Awake");
                InvokeNonPublic(firebase, "InitializeDurableStore");
                InvokeNonPublic(firebase, "RefreshSessionMetadataFromContext");

                registry.RebuildRegistry();
                var requestedGameId = ResolveCommandLineValue(ValidationGameIdArgument);
                EnsureValidationGameRegistered(registry, requestedGameId);
                InvokeNonPublic(runtime, "Awake");

                var sessionId = $"firebase-net-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                sessionContext.BeginSession("validation_patient", "validation_therapist", sessionId);
                firebase.SetSessionId(sessionId);

                var validationGameId = ResolveValidationGameId(runtime, registry);
                Debug.Log($"[FirebaseNetworkValidation] Using validation gameId='{validationGameId}'.");

                if (!runtime.SetActiveGame(validationGameId))
                {
                    throw new InvalidOperationException($"Unable to activate validation game '{validationGameId}'.");
                }

                if (!runtime.StartActiveGame())
                {
                    throw new InvalidOperationException($"Validation game '{validationGameId}' failed to start.");
                }

                runtime.PauseActiveGame();
                runtime.ResumeActiveGame();
                runtime.StopActiveGame(TheraplyCore.Games.Contracts.GameStopReason.Completed);

                firebase.FlushAllData();

                await TriggerOutboxSyncAsync(firebase, TimeSpan.FromSeconds(15), "online");
                var onlineStats = firebase.GetStatistics();
                var onlineAcceptedEvents = backend.GetAcceptedEventCount();

                if (onlineAcceptedEvents <= 0)
                {
                    throw new InvalidOperationException("Expected backend ingest acceptance during online phase, observed 0.");
                }

                if (onlineStats.outboxEventsSynced <= 0)
                {
                    throw new InvalidOperationException(
                        $"Expected synced outbox events during online phase, observed {onlineStats.outboxEventsSynced}.");
                }

                Debug.Log(
                    $"[FirebaseNetworkValidation] Online phase: accepted={onlineAcceptedEvents}, " +
                    $"outboxSynced={onlineStats.outboxEventsSynced}, pending={onlineStats.durableOutboxPending}, " +
                    $"inFlight={onlineStats.durableOutboxInFlight}.");

                await backend.StopAsync();

                QueueOfflineSessionEvents(firebase, sessionId, 4);
                firebase.FlushAllData();

                var offlineBaseline = firebase.GetStatistics();
                await TriggerOutboxSyncAsync(firebase, TimeSpan.FromSeconds(15), "offline");
                var offlineStats = firebase.GetStatistics();

                var offlineFailureDelta = offlineStats.outboxSyncFailures - offlineBaseline.outboxSyncFailures;
                var offlineRetryDelta = offlineStats.outboxRetriesScheduled - offlineBaseline.outboxRetriesScheduled;
                var offlinePending = offlineStats.durableOutboxPending;
                var offlineFailed = offlineStats.durableOutboxFailed;
                var retrySignalDetected =
                    offlineFailureDelta > 0 ||
                    offlineRetryDelta > 0 ||
                    offlinePending > 0 ||
                    offlineFailed > 0;

                if (!retrySignalDetected)
                {
                    throw new InvalidOperationException(
                        "Expected offline retry signal (failures/retries/pending/failed), but no counters changed.");
                }

                Debug.Log(
                    $"[FirebaseNetworkValidation] Offline phase: " +
                    $"failureDelta={offlineFailureDelta}, retryDelta={offlineRetryDelta}, pending={offlinePending}, failed={offlineFailed}.");

                await WaitForOutboxIdleAsync(firebase, TimeSpan.FromSeconds(8), "restart-prep");
                InvokeNonPublic(firebase, "DisposeDurableStore");
                InvokeNonPublic(firebase, "InitializeDurableStore");
                InvokeNonPublic(firebase, "RefreshSessionMetadataFromContext");
                SetNonPublicField(firebase, "_isOutboxSyncRunning", false);
                SetNonPublicField(firebase, "_lastOutboxSyncAtUtc", DateTime.UtcNow.AddMinutes(-1));

                var restartStats = firebase.GetStatistics();
                var restartBacklog =
                    restartStats.durableOutboxPending +
                    restartStats.durableOutboxInFlight +
                    restartStats.durableOutboxFailed;
                if (restartBacklog <= 0)
                {
                    throw new InvalidOperationException(
                        "Outbox backlog was not restored after durable store restart.");
                }

                Debug.Log(
                    $"[FirebaseNetworkValidation] Restart phase: pending={restartStats.durableOutboxPending}, " +
                    $"inFlight={restartStats.durableOutboxInFlight}, failed={restartStats.durableOutboxFailed}, " +
                    $"replayed={restartStats.durableOutboxReplayed}.");

                await backend.StartAsync();

                var reconnectStats = await DrainOutboxAfterReconnectAsync(firebase, TimeSpan.FromSeconds(20));
                Debug.Log(
                    $"[FirebaseNetworkValidation] Reconnect drain returned stats: pending={reconnectStats.durableOutboxPending}, " +
                    $"inFlight={reconnectStats.durableOutboxInFlight}, failed={reconnectStats.durableOutboxFailed}, replayed={reconnectStats.durableOutboxReplayed}.");
                var reconnectAcceptedEvents = backend.GetAcceptedEventCount();
                Debug.Log(
                    $"[FirebaseNetworkValidation] Reconnect accepted events: online={onlineAcceptedEvents}, reconnect={reconnectAcceptedEvents}.");

                if (reconnectStats.durableOutboxPending != 0 ||
                    reconnectStats.durableOutboxInFlight != 0 ||
                    reconnectStats.durableOutboxFailed != 0)
                {
                    throw new InvalidOperationException(
                        $"Outbox did not drain after reconnect (pending={reconnectStats.durableOutboxPending}, inFlight={reconnectStats.durableOutboxInFlight}, failed={reconnectStats.durableOutboxFailed}).");
                }

                if (reconnectAcceptedEvents <= onlineAcceptedEvents)
                {
                    throw new InvalidOperationException(
                        $"Reconnect ingest did not increase backend event count (online={onlineAcceptedEvents}, reconnect={reconnectAcceptedEvents}).");
                }

                var replayedIncreased = reconnectStats.durableOutboxReplayed > onlineStats.durableOutboxReplayed;

                await backend.StopAsync();
                InvokeNonPublic(firebase, "DisposeDurableStore");

                return
                    $"gameId={validationGameId}; session={sessionId}; onlineAccepted={onlineAcceptedEvents}; " +
                    $"onlineSynced={onlineStats.outboxEventsSynced}; " +
                    $"offlineFailureDelta={offlineFailureDelta}; " +
                    $"offlineRetryDelta={offlineRetryDelta}; " +
                    $"offlineFailed={offlineFailed}; " +
                    $"restartBacklog={restartBacklog}; " +
                    $"pendingAfterReconnect={reconnectStats.durableOutboxPending}; " +
                    $"failedAfterReconnect={reconnectStats.durableOutboxFailed}; " +
                    $"replayedAfterReconnect={reconnectStats.durableOutboxReplayed}; " +
                    $"reconnectAccepted={reconnectAcceptedEvents}; reconnectSynced={reconnectStats.outboxEventsSynced}; " +
                    $"replayedIncreased={replayedIncreased}";
            }
        }

        private static string ResolveValidationGameId(GameRuntimeService runtime, GameRegistryService registry)
        {
            var cliValue = ResolveCommandLineValue(ValidationGameIdArgument);
            if (!string.IsNullOrWhiteSpace(cliValue))
            {
                return cliValue.Trim();
            }

            if (runtime != null)
            {
                if (!string.IsNullOrWhiteSpace(runtime.ActiveGameId))
                {
                    return runtime.ActiveGameId.Trim();
                }

                var defaultGameId = ReadNonPublicString(runtime, "_defaultGameId");
                if (!string.IsNullOrWhiteSpace(defaultGameId))
                {
                    return defaultGameId.Trim();
                }
            }

            var firstRegistered = ResolveFirstRegisteredGameId(registry);
            if (!string.IsNullOrWhiteSpace(firstRegistered))
            {
                return firstRegistered.Trim();
            }

            throw new InvalidOperationException(
                $"Unable to resolve validation gameId. Pass '-{ValidationGameIdArgument}=<gameId>' to Unity CLI or configure a runtime default game.");
        }

        private static void EnsureValidationGameRegistered(
            GameRegistryService registry,
            string preferredGameId)
        {
            if (registry == null)
            {
                return;
            }

            var normalizedPreferredGameId = string.IsNullOrWhiteSpace(preferredGameId)
                ? string.Empty
                : preferredGameId.Trim();

            if (!string.IsNullOrWhiteSpace(normalizedPreferredGameId) &&
                registry.TryResolve(normalizedPreferredGameId, out _))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedPreferredGameId) &&
                !string.IsNullOrWhiteSpace(ResolveFirstRegisteredGameId(registry)))
            {
                return;
            }

            var candidateTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetLoadableTypes)
                .Where(IsValidationCandidateType)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            for (var i = 0; i < candidateTypes.Length; i++)
            {
                var type = candidateTypes[i];
                var host = new GameObject(type.Name + "_ValidationRuntime");

                try
                {
                    var behaviour = host.AddComponent(type) as MonoBehaviour;
                    if (!(behaviour is GameContracts.IGameModule module) || string.IsNullOrWhiteSpace(module.GameId))
                    {
                        UnityEngine.Object.DestroyImmediate(host);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(normalizedPreferredGameId) &&
                        !string.Equals(module.GameId, normalizedPreferredGameId, StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Object.DestroyImmediate(host);
                        continue;
                    }

                    if (!registry.RegisterRuntime(module.GameId, module))
                    {
                        UnityEngine.Object.DestroyImmediate(host);
                        if (!registry.TryResolve(module.GameId, out _))
                        {
                            continue;
                        }
                    }

                    Debug.Log(
                        $"[FirebaseNetworkValidation] Auto-registered validation game '{module.GameId}' using type '{type.FullName}'.");
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[FirebaseNetworkValidation] Failed to bootstrap candidate module '{type.FullName}': {exception.Message}");
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                return Array.Empty<Type>();
            }

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException typeLoadException)
            {
                if (typeLoadException.Types == null)
                {
                    return Array.Empty<Type>();
                }

                return typeLoadException.Types.Where(type => type != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static bool IsValidationCandidateType(Type candidateType)
        {
            if (candidateType == null || candidateType.IsAbstract || candidateType.IsGenericTypeDefinition)
            {
                return false;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(candidateType) ||
                !typeof(GameContracts.IGameModule).IsAssignableFrom(candidateType))
            {
                return false;
            }

            if (!typeof(GameContracts.IDefaultGameConfigProvider).IsAssignableFrom(candidateType))
            {
                return false;
            }

            var namespaceValue = candidateType.Namespace ?? string.Empty;
            return namespaceValue.StartsWith("TheraplyExamples", StringComparison.Ordinal);
        }

        private static string ResolveCommandLineValue(string argumentName)
        {
            if (string.IsNullOrWhiteSpace(argumentName))
            {
                return string.Empty;
            }

            var args = Environment.GetCommandLineArgs();
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            var singleDash = "-" + argumentName.Trim();
            var doubleDash = "--" + argumentName.Trim();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (arg.StartsWith(singleDash + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(singleDash.Length + 1);
                }

                if (arg.StartsWith(doubleDash + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(doubleDash.Length + 1);
                }

                if ((string.Equals(arg, singleDash, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg, doubleDash, StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            return string.Empty;
        }

        private static string ResolveFirstRegisteredGameId(GameRegistryService registry)
        {
            if (registry == null)
            {
                return string.Empty;
            }

            var modulesField = registry.GetType().GetField("_modules", BindingFlags.Instance | BindingFlags.NonPublic);
            if (modulesField?.GetValue(registry) is IDictionary moduleMap)
            {
                foreach (DictionaryEntry entry in moduleMap)
                {
                    if (entry.Key is string key && !string.IsNullOrWhiteSpace(key))
                    {
                        return key;
                    }
                }
            }

            var entriesField = registry.GetType().GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(entriesField?.GetValue(registry) is IEnumerable entries))
            {
                return string.Empty;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var gameIdField = entry.GetType().GetField("gameId", BindingFlags.Instance | BindingFlags.Public);
                if (!(gameIdField?.GetValue(entry) is string gameId) || string.IsNullOrWhiteSpace(gameId))
                {
                    continue;
                }

                return gameId;
            }

            return string.Empty;
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(projectRoot, "Temp", "CliValidation", "firebase_network_validation_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log($"[FirebaseNetworkValidation] Result file: {outputPath}");
        }

        private static void ConfigureFirebase(FirebaseDataService firebase, int port)
        {
            var baseUrl = $"http://127.0.0.1:{port}";
            var durableFolder = $"session_resilience_validation_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            SetNonPublicField(firebase, "_simulateFirebase", false);
            SetNonPublicField(firebase, "_sessionIngestEndpointUrl", $"{baseUrl}/session-ingest");
            SetNonPublicField(firebase, "_sessionReconciliationEndpointUrl", $"{baseUrl}/session-reconciliation");
            SetNonPublicField(firebase, "_firebaseAuthBearerToken", string.Empty);
            SetNonPublicField(firebase, "_firebaseApiKey", string.Empty);
            SetNonPublicField(firebase, "_localDurableFolder", durableFolder);
            SetNonPublicField(firebase, "_localDurableFileName", "events.ndjson");
            SetNonPublicField(firebase, "_sqliteStoreFileName", "session_events.db");
            SetNonPublicField(firebase, "_enableOutboxSync", false);
            SetNonPublicField(firebase, "_outboxBatchSize", 256);
            SetNonPublicField(firebase, "_outboxSyncIntervalSeconds", 0.25f);
            SetNonPublicField(firebase, "_logOutboxSync", true);
            SetNonPublicField(firebase, "_logFirebaseBackendPayloads", true);
            SetNonPublicField(firebase, "_logFirebaseBackendDiagnostics", true);
            SetNonPublicField(firebase, "_lastOutboxSyncAtUtc", DateTime.UtcNow.AddMinutes(-1));
            SetNonPublicField(firebase, "_isOutboxSyncRunning", false);
            EditorUtility.SetDirty(firebase);
        }

        private static async Task TriggerOutboxSyncAsync(
            FirebaseDataService firebase,
            TimeSpan timeout,
            string phase)
        {
            Debug.Log(
                $"[FirebaseNetworkValidation] TriggerOutboxSync phase='{phase}' started (timeout={timeout.TotalSeconds:F1}s).");
            InvokeNonPublic(firebase, "TriggerOutboxSync");
            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                if (!ReadNonPublicBool(firebase, "_isOutboxSyncRunning"))
                {
                    var elapsed = DateTime.UtcNow - startedAt;
                    Debug.Log(
                        $"[FirebaseNetworkValidation] TriggerOutboxSync phase='{phase}' completed in {elapsed.TotalSeconds:F2}s.");
                    return;
                }

                await Task.Delay(100);
            }

            var timeoutStats = firebase.GetStatistics();
            throw new TimeoutException(
                $"Outbox sync did not complete in phase '{phase}' within {timeout.TotalSeconds:F1}s " +
                $"(pending={timeoutStats.durableOutboxPending}, inFlight={timeoutStats.durableOutboxInFlight}, failed={timeoutStats.durableOutboxFailed}).");
        }

        private static async Task WaitForOutboxIdleAsync(
            FirebaseDataService firebase,
            TimeSpan timeout,
            string phase)
        {
            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                if (!ReadNonPublicBool(firebase, "_isOutboxSyncRunning"))
                {
                    return;
                }

                await Task.Delay(100);
            }

            throw new TimeoutException(
                $"Outbox worker did not become idle before phase '{phase}' within {timeout.TotalSeconds:F1}s.");
        }

        private static async Task<QueueStatistics> DrainOutboxAfterReconnectAsync(FirebaseDataService firebase, TimeSpan timeout)
        {
            var startedAt = DateTime.UtcNow;
            var iteration = 0;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                iteration++;
                Debug.Log(
                    $"[FirebaseNetworkValidation] Reconnect drain iteration={iteration} started.");
                await TriggerOutboxSyncAsync(firebase, TimeSpan.FromSeconds(8), "reconnect");
                var stats = firebase.GetStatistics();
                Debug.Log(
                    $"[FirebaseNetworkValidation] Reconnect drain iteration={iteration} stats: " +
                    $"pending={stats.durableOutboxPending}, inFlight={stats.durableOutboxInFlight}, failed={stats.durableOutboxFailed}.");
                if (stats.durableOutboxPending == 0 &&
                    stats.durableOutboxInFlight == 0 &&
                    stats.durableOutboxFailed == 0)
                {
                    Debug.Log(
                        $"[FirebaseNetworkValidation] Reconnect drain completed in iteration={iteration}.");
                    return stats;
                }

                await Task.Delay(300);
            }

            var finalStats = firebase.GetStatistics();
            throw new TimeoutException(
                $"Outbox did not drain after reconnect (pending={finalStats.durableOutboxPending}, inFlight={finalStats.durableOutboxInFlight}, failed={finalStats.durableOutboxFailed}).");
        }

        private static void QueueOfflineSessionEvents(FirebaseDataService firebase, string sessionId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                firebase.QueueDataPoint(new GameDataPoint
                {
                    timestamp = DateTime.UtcNow,
                    dataType = i % 2 == 0 ? "game_start" : "game_end",
                    payload = new Dictionary<string, object>
                    {
                        { "sessionId", sessionId },
                        { "offlineIndex", i },
                        { "phase", "offline" },
                    },
                });
            }
        }

        private static void InvokeNonPublic(object target, string methodName)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            method.Invoke(target, null);
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        private static bool ReadNonPublicBool(object target, string fieldName)
        {
            if (target == null) return false;

            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return false;
            }

            return field.GetValue(target) is bool value && value;
        }

        private static string ReadNonPublicString(object target, string fieldName)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return string.Empty;
            }

            return field.GetValue(target) as string ?? string.Empty;
        }

        private static int ReserveTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var endpoint = (IPEndPoint)listener.LocalEndpoint;
                return endpoint.Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed class LocalValidationBackend : IDisposable
        {
            private readonly int _port;
            private readonly Dictionary<string, Dictionary<long, string>> _sessionEvents =
                new Dictionary<string, Dictionary<long, string>>(StringComparer.Ordinal);
            private readonly HashSet<string> _knownEventIds =
                new HashSet<string>(StringComparer.Ordinal);
            private readonly object _lock = new object();

            private HttpListener _listener;
            private CancellationTokenSource _cancellation;
            private Task _serverTask;
            private bool _disposed;

            public LocalValidationBackend(int port)
            {
                _port = port;
            }

            public int GetAcceptedEventCount()
            {
                lock (_lock)
                {
                    return _knownEventIds.Count;
                }
            }

            public Task StartAsync()
            {
                ThrowIfDisposed();
                if (_listener != null)
                {
                    return Task.CompletedTask;
                }

                _cancellation = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _serverTask = Task.Run(() => RunServerLoopAsync(_cancellation.Token));

                Debug.Log($"[FirebaseNetworkValidation] Local backend started on port {_port}.");
                return Task.CompletedTask;
            }

            public async Task StopAsync()
            {
                if (_listener == null)
                {
                    return;
                }

                try
                {
                    _cancellation.Cancel();
                    _listener.Stop();
                    _listener.Close();
                    if (_serverTask != null)
                    {
                        var completedTask = await Task.WhenAny(_serverTask, Task.Delay(TimeSpan.FromSeconds(2)));
                        if (!ReferenceEquals(completedTask, _serverTask))
                        {
                            Debug.LogWarning(
                                "[FirebaseNetworkValidation] Backend server shutdown timed out; continuing teardown.");
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore shutdown races.
                }
                finally
                {
                    _listener = null;
                    _serverTask = null;
                    _cancellation.Dispose();
                    _cancellation = null;
                }

                Debug.Log("[FirebaseNetworkValidation] Local backend stopped.");
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                StopAsync().GetAwaiter().GetResult();
            }

            private async Task RunServerLoopAsync(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    try
                    {
                        await HandleRequestAsync(context, cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[FirebaseNetworkValidation] Backend handler error: {exception.Message}");
                        context.Response.StatusCode = 500;
                        await WriteJsonAsync(context.Response, "{\"success\":false,\"errorCode\":\"BACKEND_EXCEPTION\"}");
                    }
                }
            }

            private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
            {
                var requestPath = context.Request.Url == null
                    ? string.Empty
                    : context.Request.Url.AbsolutePath.Trim().ToLowerInvariant();
                var body = await ReadBodyAsync(context.Request, cancellationToken);

                if (requestPath.EndsWith("/session-ingest", StringComparison.Ordinal))
                {
                    var response = HandleIngest(body);
                    await WriteJsonAsync(context.Response, JsonUtility.ToJson(response));
                    return;
                }

                if (requestPath.EndsWith("/session-reconciliation", StringComparison.Ordinal))
                {
                    var response = HandleReconciliation(body);
                    await WriteJsonAsync(context.Response, JsonUtility.ToJson(response));
                    return;
                }

                context.Response.StatusCode = 404;
                await WriteJsonAsync(context.Response, "{\"success\":false,\"errorCode\":\"NOT_FOUND\"}");
            }

            private IngestResponse HandleIngest(string body)
            {
                var request = JsonUtility.FromJson<IngestRequest>(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var response = new IngestResponse
                {
                    success = true,
                    errorCode = string.Empty,
                    eventResults = new List<IngestEventResult>(),
                };

                if (request == null || request.events == null)
                {
                    return response;
                }

                lock (_lock)
                {
                    for (var i = 0; i < request.events.Count; i++)
                    {
                        var evt = request.events[i];
                        if (evt == null || string.IsNullOrWhiteSpace(evt.eventId))
                        {
                            response.eventResults.Add(new IngestEventResult
                            {
                                eventId = evt?.eventId ?? string.Empty,
                                status = "RETRY",
                                reasonCode = "INVALID_EVENT_ID",
                            });
                            continue;
                        }

                        if (_knownEventIds.Contains(evt.eventId))
                        {
                            response.eventResults.Add(new IngestEventResult
                            {
                                eventId = evt.eventId,
                                status = "DUPLICATE",
                                reasonCode = "EVENT_ID_ALREADY_EXISTS",
                            });
                            continue;
                        }

                        _knownEventIds.Add(evt.eventId);
                        if (!_sessionEvents.TryGetValue(evt.sessionId ?? string.Empty, out var sequenceMap))
                        {
                            sequenceMap = new Dictionary<long, string>();
                            _sessionEvents[evt.sessionId ?? string.Empty] = sequenceMap;
                        }

                        sequenceMap[evt.sequence] = evt.eventId;
                        response.eventResults.Add(new IngestEventResult
                        {
                            eventId = evt.eventId,
                            status = "ACCEPTED",
                            reasonCode = string.Empty,
                        });
                    }
                }

                return response;
            }

            private ReconciliationResponse HandleReconciliation(string body)
            {
                var request = JsonUtility.FromJson<ReconciliationRequest>(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var response = new ReconciliationResponse
                {
                    success = true,
                    errorCode = string.Empty,
                    events = new List<ReconciliationEvent>(),
                };

                if (request == null || string.IsNullOrWhiteSpace(request.sessionId))
                {
                    response.success = false;
                    response.errorCode = "SESSION_ID_REQUIRED";
                    return response;
                }

                lock (_lock)
                {
                    if (!_sessionEvents.TryGetValue(request.sessionId, out var sequenceMap))
                    {
                        return response;
                    }

                    var sequences = new List<long>(sequenceMap.Keys);
                    sequences.Sort();
                    for (var i = 0; i < sequences.Count; i++)
                    {
                        var sequence = sequences[i];
                        sequenceMap.TryGetValue(sequence, out var eventId);
                        response.events.Add(new ReconciliationEvent
                        {
                            eventId = eventId ?? string.Empty,
                            sequence = sequence,
                        });
                    }
                }

                return response;
            }

            private static async Task<string> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
            {
                if (request?.InputStream == null)
                {
                    return string.Empty;
                }

                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                {
                    var readTask = reader.ReadToEndAsync();
                    while (!readTask.IsCompleted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(10, cancellationToken);
                    }

                    return readTask.Result ?? string.Empty;
                }
            }

            private static async Task WriteJsonAsync(HttpListenerResponse response, string json)
            {
                var payload = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                response.ContentType = "application/json";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = payload.Length;
                response.StatusCode = response.StatusCode == 0 ? 200 : response.StatusCode;
                await response.OutputStream.WriteAsync(payload, 0, payload.Length);
                response.OutputStream.Close();
            }

            private void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LocalValidationBackend));
                }
            }

            [Serializable]
            private sealed class IngestRequest
            {
                public List<IngestEvent> events;
            }

            [Serializable]
            private sealed class IngestEvent
            {
                public string eventId;
                public string sessionId;
                public long sequence;
            }

            [Serializable]
            private sealed class IngestResponse
            {
                public bool success;
                public string errorCode;
                public List<IngestEventResult> eventResults;
            }

            [Serializable]
            private sealed class IngestEventResult
            {
                public string eventId;
                public string status;
                public string reasonCode;
            }

            [Serializable]
            private sealed class ReconciliationRequest
            {
                public string sessionId;
            }

            [Serializable]
            private sealed class ReconciliationResponse
            {
                public bool success;
                public string errorCode;
                public List<ReconciliationEvent> events;
            }

            [Serializable]
            private sealed class ReconciliationEvent
            {
                public string eventId;
                public long sequence;
            }
        }
    }
}
