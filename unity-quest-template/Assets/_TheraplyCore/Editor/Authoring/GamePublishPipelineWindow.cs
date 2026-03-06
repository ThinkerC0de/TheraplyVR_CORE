using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheraplyCore.Editor.Automation;
using TheraplyCore.Games.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace TheraplyCore.Editor.Authoring
{
    /// <summary>
    /// One-click publish pipeline that replaces the need to run each tool separately:
    ///
    ///   [1] Export Contracts
    ///       1a. SessionFlowAuthoringExport  → game_definition_*.json, mobile_control_schema_*.json
    ///       1b. MobileControlLayoutAsset    → mobile_control_layout_*.json (headless, no dialog)
    ///
    ///   [2] Build Asset Bundle + .pkg.json manifest
    ///
    ///   [3] Sync Firebase
    ///       3a. sync_mobile_control_schemas_to_catalog.ps1 → injects layouts into game_catalog_seed.json
    ///       3b. sync_admin_console_seed_assets.ps1
    ///       3c. sync_game_catalog_to_firebase.js → upserts game_catalog/{gameId} in Firestore
    ///
    ///   After pipeline: upload hosting/public/content/ to your server.
    /// </summary>
    public sealed class GamePublishPipelineWindow : EditorWindow
    {
        private const string MenuPath = "Theraply/Session Flow/Export/One-Click Publish Pipeline";
        private const string PrefKeyPrefix = "Theraply.PublishPipeline.";
        private const string DefaultProjectId = "theraply-vr-demo";
        private const string DefaultBaseUrl = "https://pranasense.pl/content";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ── Settings ────────────────────────────────────────────────────────
        private string _gameId = string.Empty;
        private string _contentVersion = "1.0.0";
        private string _basePackageUrl = DefaultBaseUrl;
        private string _firebaseProjectId = DefaultProjectId;
        private SceneAsset _sceneAsset;
        private string _outputDirectory = string.Empty;
        private BuildTarget _buildTarget = BuildTarget.Android;
        private bool _exportContracts = true;
        private bool _exportLayouts = true;
        private bool _syncFirebase = true;
        private bool _deleteMissingFromFirestore;
        private bool _showAdvanced;

        // ── Runtime state ────────────────────────────────────────────────────
        private bool _isRunning;
        private Vector2 _logScroll;
        private string _log = string.Empty;
        private StepState _stepExport  = StepState.Idle;
        private StepState _stepBundle  = StepState.Idle;
        private StepState _stepFirebase = StepState.Idle;

        // ── Game ID discovery ────────────────────────────────────────────────
        private string[] _discoveredGameIds = new string[0];
        private int _selectedGameIdIndex;

        private enum StepState { Idle, Running, Pass, Fail, Skipped }

        // ─── Menu ────────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            var window = GetWindow<GamePublishPipelineWindow>("Publish Pipeline");
            window.minSize = new Vector2(740f, 640f);
            window.Show();
        }

        // ─── Unity lifecycle ─────────────────────────────────────────────────

        private void OnEnable()
        {
            LoadPrefs();
            EnsureDefaultOutputDirectory();
            DiscoverGameIds();
            if (_sceneAsset == null)
                TryUseActiveScene();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        // ─── GUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.LabelField("One-Click Publish Pipeline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Exports contracts + layouts → builds asset bundle → syncs Firebase catalog.\n" +
                "After pipeline: upload  hosting/public/content/  to your server.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_isRunning))
            {
                DrawEssentialSettings();
                EditorGUILayout.Space(6f);
                DrawAdvancedFoldout();
                EditorGUILayout.Space(6f);
                DrawPipelineStepToggles();
                EditorGUILayout.Space(8f);
                DrawActionButtons();
            }

            EditorGUILayout.Space(6f);
            DrawStepStatus();
            EditorGUILayout.Space(4f);
            DrawLog();
        }

        private void DrawEssentialSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            if (_discoveredGameIds.Length > 0)
            {
                var newIndex = EditorGUILayout.Popup("Game Id", _selectedGameIdIndex, _discoveredGameIds);
                if (newIndex != _selectedGameIdIndex)
                {
                    _selectedGameIdIndex = newIndex;
                    _gameId = _discoveredGameIds[newIndex];
                }
            }
            else
            {
                _gameId = EditorGUILayout.TextField("Game Id", _gameId ?? string.Empty);
            }

            _contentVersion     = EditorGUILayout.TextField("Content Version",   _contentVersion     ?? string.Empty);
            _basePackageUrl     = EditorGUILayout.TextField("Base Package URL",  _basePackageUrl     ?? string.Empty);
            _firebaseProjectId  = EditorGUILayout.TextField("Firebase Project ID", _firebaseProjectId ?? string.Empty);

            var nextScene = EditorGUILayout.ObjectField(
                "Scene", _sceneAsset, typeof(SceneAsset), false) as SceneAsset;
            if (nextScene != _sceneAsset)
                _sceneAsset = nextScene;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Use Active Scene", GUILayout.Width(150f)))
                TryUseActiveScene();
            if (GUILayout.Button("Refresh Game IDs", GUILayout.Width(140f)))
                DiscoverGameIds();
            if (GUILayout.Button("Mobile Layout Editor", GUILayout.Width(160f)))
                MobileControlLayoutEditorWindow.OpenWindow();
            if (GUILayout.Button("Catalog Editor", GUILayout.Width(120f)))
                GameCatalogEditorWindow.OpenWindow();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAdvancedFoldout()
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced Settings", true);
            if (!_showAdvanced)
                return;

            EditorGUI.indentLevel++;
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", _buildTarget);

            EditorGUILayout.BeginHorizontal();
            _outputDirectory = EditorGUILayout.TextField("Output Directory", _outputDirectory ?? string.Empty);
            if (GUILayout.Button("Browse", GUILayout.Width(72f)))
            {
                var selected = EditorUtility.OpenFolderPanel(
                    "Select output directory", _outputDirectory ?? string.Empty, string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                    _outputDirectory = selected.Trim();
            }
            EditorGUILayout.EndHorizontal();

            _deleteMissingFromFirestore = EditorGUILayout.ToggleLeft(
                "Delete stale Firestore docs not present in seed", _deleteMissingFromFirestore);
            EditorGUI.indentLevel--;
        }

        private void DrawPipelineStepToggles()
        {
            EditorGUILayout.LabelField("Pipeline Steps", EditorStyles.boldLabel);

            _exportContracts = EditorGUILayout.ToggleLeft(
                "[1a] Export Game Definitions → contracts/", _exportContracts);
            _exportLayouts = EditorGUILayout.ToggleLeft(
                "[1b] Export Mobile Control Layouts → contracts/  (headless, from assets in project)",
                _exportLayouts);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ToggleLeft("[2 ] Build Asset Bundle + Manifest  (always runs)", true);

            _syncFirebase = EditorGUILayout.ToggleLeft(
                "[3 ] Sync Firebase Catalog  (PS scripts + node)", _syncFirebase);
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save Settings", GUILayout.Width(120f)))
            {
                SavePrefs();
                ShowNotification(new GUIContent("Settings saved"));
            }

            if (GUILayout.Button("Dry Run", GUILayout.Width(100f)))
                RunPipeline(dryRun: true);

            var buildLabel = _syncFirebase ? "Build + Sync Firebase" : "Build Only";
            if (GUILayout.Button(buildLabel, GUILayout.Width(180f)))
                RunPipeline(dryRun: false);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStepStatus()
        {
            EditorGUILayout.LabelField("Step Status", EditorStyles.boldLabel);
            DrawStepRow("[1] Export Contracts + Layouts", _stepExport);
            DrawStepRow("[2] Build Asset Bundle",         _stepBundle);
            DrawStepRow("[3] Sync Firebase",              _stepFirebase);
        }

        private static void DrawStepRow(string label, StepState state)
        {
            string icon;
            Color color;
            switch (state)
            {
                case StepState.Running: icon = ">>"; color = Color.yellow; break;
                case StepState.Pass:    icon = "OK"; color = Color.green;  break;
                case StepState.Fail:    icon = "!!"; color = Color.red;    break;
                case StepState.Skipped: icon = "--"; color = Color.gray;   break;
                default:                icon = "  "; color = Color.white;  break;
            }
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField("[" + icon + "]  " + label);
            GUI.contentColor = prev;
        }

        private void DrawLog()
        {
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(190f));
            EditorGUILayout.TextArea(
                string.IsNullOrWhiteSpace(_log) ? "No pipeline run yet." : _log,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ─── Pipeline ─────────────────────────────────────────────────────────

        private void RunPipeline(bool dryRun)
        {
            if (!ValidateInputs(out var validationError))
            {
                EditorUtility.DisplayDialog("Publish Pipeline", "Validation failed:\n" + validationError, "OK");
                return;
            }

            SavePrefs();
            _isRunning = true;
            _stepExport   = StepState.Idle;
            _stepBundle   = StepState.Idle;
            _stepFirebase = StepState.Idle;

            var repoRoot = ResolveRepoRoot();
            var log = new StringBuilder(2048);
            log.AppendLine("[INFO] Mode:            " + (dryRun ? "DRY-RUN" : "LIVE"));
            log.AppendLine("[INFO] Game Id:          " + _gameId);
            log.AppendLine("[INFO] Content Version:  " + _contentVersion);
            log.AppendLine("[INFO] Repo root:        " + repoRoot);

            try
            {
                // ── Step 1: Export ────────────────────────────────────────────
                _stepExport = StepState.Running;
                Repaint();

                if (_exportContracts)
                {
                    EditorUtility.DisplayProgressBar("Publish Pipeline", "Exporting game definitions...", 0.05f);
                    var exportSummary = SessionFlowAuthoringExport.ExportAllToContracts();
                    log.AppendLine("[PASS] Game definitions: " + exportSummary);
                }
                else
                {
                    log.AppendLine("[SKIP] Game definitions export.");
                }

                if (_exportLayouts)
                {
                    EditorUtility.DisplayProgressBar("Publish Pipeline", "Exporting mobile layouts...", 0.12f);
                    var layoutSummary = ExportMobileLayouts(repoRoot, log);
                    log.AppendLine("[PASS] Mobile layouts: " + layoutSummary);
                }
                else
                {
                    log.AppendLine("[SKIP] Mobile layouts export.");
                }

                _stepExport = (_exportContracts || _exportLayouts) ? StepState.Pass : StepState.Skipped;

                // ── Step 2: Build asset bundle ────────────────────────────────
                _stepBundle = StepState.Running;
                Repaint();
                EditorUtility.DisplayProgressBar("Publish Pipeline", "Building asset bundle...", 0.30f);

                var scenePath = _sceneAsset == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(_sceneAsset);

                var request = new AssetBundlePackageBuilder.EditorBuildRequest
                {
                    gameId        = (_gameId         ?? string.Empty).Trim(),
                    contentVersion = (_contentVersion ?? string.Empty).Trim(),
                    sceneAssetPath = scenePath,
                    outputDirectory = (_outputDirectory ?? string.Empty).Trim(),
                    basePackageUrl  = (_basePackageUrl  ?? string.Empty).Trim(),
                    buildTarget     = _buildTarget.ToString(),
                };

                var buildResult = AssetBundlePackageBuilder.BuildPackageFromEditor(request);
                log.AppendLine("[PASS] Bundle:      " + buildResult.bundlePath);
                log.AppendLine("[PASS] Manifest:    " + buildResult.manifestPath);
                log.AppendLine("[PASS] Package URI: " + buildResult.packageUri);
                _stepBundle = StepState.Pass;

                // ── Step 3: Sync Firebase ─────────────────────────────────────
                if (_syncFirebase)
                {
                    _stepFirebase = StepState.Running;
                    Repaint();

                    // 3a — inject layouts into catalog seed
                    EditorUtility.DisplayProgressBar("Publish Pipeline", "Syncing mobile control schemas...", 0.60f);
                    var schemaSyncScript = Path.Combine(repoRoot, "scripts", "sync_mobile_control_schemas_to_catalog.ps1");
                    if (File.Exists(schemaSyncScript))
                    {
                        RunPowerShellScript(schemaSyncScript, "-RepoRoot " + QuoteArg(repoRoot), repoRoot, log);
                        log.AppendLine("[PASS] sync_mobile_control_schemas_to_catalog.ps1");
                    }
                    else
                    {
                        log.AppendLine("[SKIP] sync_mobile_control_schemas_to_catalog.ps1 not found.");
                    }

                    // 3b — sync admin console seed assets
                    EditorUtility.DisplayProgressBar("Publish Pipeline", "Syncing admin seed assets...", 0.70f);
                    var adminSyncScript = Path.Combine(repoRoot, "scripts", "sync_admin_console_seed_assets.ps1");
                    if (File.Exists(adminSyncScript))
                    {
                        RunPowerShellScript(adminSyncScript, "-RepoRoot " + QuoteArg(repoRoot), repoRoot, log);
                        log.AppendLine("[PASS] sync_admin_console_seed_assets.ps1");
                    }
                    else
                    {
                        log.AppendLine("[SKIP] sync_admin_console_seed_assets.ps1 not found.");
                    }

                    // 3c — upsert game_catalog to Firestore
                    EditorUtility.DisplayProgressBar(
                        "Publish Pipeline",
                        dryRun ? "Firebase dry-run..." : "Syncing Firebase catalog...",
                        0.82f);
                    RunFirebaseNodeSync(repoRoot, (_firebaseProjectId ?? string.Empty).Trim(),
                        dryRun, _deleteMissingFromFirestore, log);
                    log.AppendLine("[PASS] Firebase catalog synced" + (dryRun ? " (dry-run)." : "."));
                    _stepFirebase = StepState.Pass;
                }
                else
                {
                    _stepFirebase = StepState.Skipped;
                    log.AppendLine("[SKIP] Firebase sync.");
                }

                EditorUtility.DisplayProgressBar("Publish Pipeline", "Finalizing...", 0.97f);
                AssetDatabase.Refresh();
                _log = log.ToString();
                Repaint();

                EditorUtility.DisplayDialog(
                    "Publish Pipeline",
                    "Pipeline completed successfully.\n\nNext step: upload  hosting/public/content/  to your server.",
                    "OK");
            }
            catch (Exception ex)
            {
                if (_stepExport   == StepState.Running) _stepExport   = StepState.Fail;
                if (_stepBundle   == StepState.Running) _stepBundle   = StepState.Fail;
                if (_stepFirebase == StepState.Running) _stepFirebase = StepState.Fail;

                log.AppendLine("[FAIL] " + ex.Message);
                _log = log.ToString();
                Repaint();

                Debug.LogError("[GamePublishPipeline] " + ex);
                EditorUtility.DisplayDialog("Publish Pipeline", "Pipeline failed:\n" + ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isRunning = false;
                Repaint();
            }
        }

        private bool ValidateInputs(out string error)
        {
            if (string.IsNullOrWhiteSpace(_gameId))
            { error = "Game Id is required."; return false; }
            if (string.IsNullOrWhiteSpace(_contentVersion))
            { error = "Content Version is required."; return false; }
            if (string.IsNullOrWhiteSpace(_basePackageUrl))
            { error = "Base Package URL is required."; return false; }
            if (_sceneAsset == null)
            { error = "Scene is required. Use 'Use Active Scene' or assign a SceneAsset."; return false; }
            if (string.IsNullOrWhiteSpace(_outputDirectory))
            { error = "Output Directory is required."; return false; }
            if (_syncFirebase && string.IsNullOrWhiteSpace(_firebaseProjectId))
            { error = "Firebase Project ID is required when Sync Firebase is enabled."; return false; }
            error = null;
            return true;
        }

        // ─── Headless mobile layout export ────────────────────────────────────

        /// <summary>
        /// Finds all MobileControlLayoutAsset in the project and exports each one to
        ///   contracts/mobile_control_layout_{gameId}.json
        ///   contracts/mobile_control_schema_{gameId}.json
        /// without opening any dialog or editor window.
        /// </summary>
        private static string ExportMobileLayouts(string repoRoot, StringBuilder log)
        {
            var contractsDir = Path.Combine(repoRoot, "contracts");
            Directory.CreateDirectory(contractsDir);

            var exported = 0;
            var warnings = 0;
            var guids = AssetDatabase.FindAssets("t:MobileControlLayoutAsset");

            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<MobileControlLayoutAsset>(assetPath);
                if (asset == null || asset.contract == null)
                    continue;

                var gameId = (asset.contract.gameId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(gameId))
                {
                    log.AppendLine("[WARN] MobileControlLayoutAsset has no gameId: " + assetPath);
                    warnings++;
                    continue;
                }

                // Validate before export
                if (!MobileControlLayoutValidator.TryValidate(asset.contract, out var layoutReason))
                {
                    log.AppendLine("[WARN] Layout validation failed for '" + gameId + "': " + layoutReason);
                    warnings++;
                    continue;
                }

                // Export layout JSON
                var safeId = SanitizeId(gameId);
                var layoutPath = Path.Combine(contractsDir, "mobile_control_layout_" + safeId + ".json");
                WriteJsonFile(layoutPath, asset.contract);
                log.AppendLine("[PASS] Layout: " + layoutPath);

                // Export schema JSON
                if (MobileControlLayoutConverter.TryToMobileControlSchema(
                    asset.contract, out var schema, out var schemaReason))
                {
                    var schemaPath = Path.Combine(contractsDir, "mobile_control_schema_" + safeId + ".json");
                    WriteJsonFile(schemaPath, schema);
                    log.AppendLine("[PASS] Schema: " + schemaPath);
                }
                else
                {
                    log.AppendLine("[WARN] Schema conversion failed for '" + gameId + "': " + schemaReason);
                    warnings++;
                }

                exported++;
            }

            return "exported=" + exported + " warnings=" + warnings;
        }

        // ─── Firebase sync helpers ────────────────────────────────────────────

        private static void RunFirebaseNodeSync(
            string repoRoot, string projectId, bool dryRun, bool deleteMissing, StringBuilder log)
        {
            var scriptPath = Path.Combine(repoRoot, "scripts", "sync_game_catalog_to_firebase.js");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Node sync script not found.", scriptPath);

            var args = new StringBuilder();
            args.Append(QuoteArg(scriptPath));
            args.Append(" --repo-root ").Append(QuoteArg(repoRoot));
            args.Append(" --project-id ").Append(QuoteArg(projectId));
            if (dryRun)        args.Append(" --dry-run");
            if (deleteMissing) args.Append(" --delete-missing");

            RunProcess("node", args.ToString(), repoRoot, log);
        }

        private static void RunPowerShellScript(
            string scriptPath, string scriptArgs, string workingDirectory, StringBuilder log)
        {
            var args = "-NoProfile -ExecutionPolicy Bypass -File " +
                       QuoteArg(scriptPath) + " " + scriptArgs;
            RunProcess("powershell", args, workingDirectory, log);
        }

        private static void RunProcess(
            string fileName, string arguments, string workingDirectory, StringBuilder log)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = new Process())
            {
                process.StartInfo = psi;
                var stdOut = new StringBuilder(256);
                var stdErr = new StringBuilder(256);
                process.OutputDataReceived += (_, a) => { if (!string.IsNullOrEmpty(a.Data)) stdOut.AppendLine(a.Data); };
                process.ErrorDataReceived  += (_, a) => { if (!string.IsNullOrEmpty(a.Data)) stdErr.AppendLine(a.Data); };

                if (!process.Start())
                    throw new InvalidOperationException("Failed to start process: " + fileName);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                var stdout = stdOut.ToString();
                var stderr = stdErr.ToString();
                if (!string.IsNullOrWhiteSpace(stdout)) log.AppendLine(stdout.TrimEnd());
                if (!string.IsNullOrWhiteSpace(stderr)) log.AppendLine(stderr.TrimEnd());

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        "Command failed (exit " + process.ExitCode + "): " + fileName);
            }
        }

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        // ─── File helpers ─────────────────────────────────────────────────────

        private static void WriteJsonFile<T>(string path, T payload)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(payload, true);
            File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
        }

        private static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
            {
                if (Array.IndexOf(invalidChars, c) >= 0) continue;
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c))
                    sb.Append('_');
            }
            return sb.ToString();
        }

        // ─── Discovery & helpers ──────────────────────────────────────────────

        private void DiscoverGameIds()
        {
            var ids = new List<string>();

            var guids = AssetDatabase.FindAssets("t:GameDefinitionAsset");
            foreach (var guid in guids)
            {
                var path  = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(path);
                if (asset?.definition != null && !string.IsNullOrWhiteSpace(asset.definition.gameId))
                {
                    var id = asset.definition.gameId.Trim();
                    if (!ids.Contains(id)) ids.Add(id);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            _discoveredGameIds = ids.ToArray();

            if (_discoveredGameIds.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(_gameId))
                    _gameId = _discoveredGameIds[0];
                _selectedGameIdIndex = Array.IndexOf(_discoveredGameIds, _gameId);
                if (_selectedGameIdIndex < 0) _selectedGameIdIndex = 0;
            }
        }

        private void TryUseActiveScene()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrWhiteSpace(activeScene.path)) return;
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(activeScene.path);
            if (asset != null) _sceneAsset = asset;
        }

        private void EnsureDefaultOutputDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_outputDirectory)) return;
            var repoRoot = ResolveRepoRoot();
            if (!string.IsNullOrWhiteSpace(repoRoot))
                _outputDirectory = Path.Combine(repoRoot, "hosting", "public", "content");
        }

        private static string ResolveRepoRoot()
        {
            var unityProjectRoot = Directory.GetParent(Application.dataPath);
            var repoRoot = unityProjectRoot?.Parent;
            return repoRoot == null ? string.Empty : repoRoot.FullName;
        }

        // ─── Prefs ────────────────────────────────────────────────────────────

        private void LoadPrefs()
        {
            _gameId              = EditorPrefs.GetString(PrefKeyPrefix + "GameId",            _gameId);
            _contentVersion      = EditorPrefs.GetString(PrefKeyPrefix + "ContentVersion",    _contentVersion);
            _basePackageUrl      = EditorPrefs.GetString(PrefKeyPrefix + "BasePackageUrl",    _basePackageUrl);
            _firebaseProjectId   = EditorPrefs.GetString(PrefKeyPrefix + "FirebaseProjectId", _firebaseProjectId);
            _outputDirectory     = EditorPrefs.GetString(PrefKeyPrefix + "OutputDirectory",   _outputDirectory);
            _exportContracts     = EditorPrefs.GetBool  (PrefKeyPrefix + "ExportContracts",   _exportContracts);
            _exportLayouts       = EditorPrefs.GetBool  (PrefKeyPrefix + "ExportLayouts",     _exportLayouts);
            _syncFirebase        = EditorPrefs.GetBool  (PrefKeyPrefix + "SyncFirebase",      _syncFirebase);
            _deleteMissingFromFirestore = EditorPrefs.GetBool(PrefKeyPrefix + "DeleteMissing", _deleteMissingFromFirestore);

            var buildTargetName = EditorPrefs.GetString(PrefKeyPrefix + "BuildTarget", _buildTarget.ToString());
            if (Enum.TryParse(buildTargetName, true, out BuildTarget parsed))
                _buildTarget = parsed;

            var scenePath = EditorPrefs.GetString(PrefKeyPrefix + "ScenePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(scenePath))
                _sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefKeyPrefix + "GameId",            (_gameId            ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "ContentVersion",    (_contentVersion    ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "BasePackageUrl",    (_basePackageUrl    ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "FirebaseProjectId", (_firebaseProjectId ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "OutputDirectory",   (_outputDirectory   ?? string.Empty).Trim());
            EditorPrefs.SetBool  (PrefKeyPrefix + "ExportContracts",   _exportContracts);
            EditorPrefs.SetBool  (PrefKeyPrefix + "ExportLayouts",     _exportLayouts);
            EditorPrefs.SetBool  (PrefKeyPrefix + "SyncFirebase",      _syncFirebase);
            EditorPrefs.SetBool  (PrefKeyPrefix + "DeleteMissing",     _deleteMissingFromFirestore);
            EditorPrefs.SetString(PrefKeyPrefix + "BuildTarget",       _buildTarget.ToString());
            EditorPrefs.SetString(PrefKeyPrefix + "ScenePath",
                _sceneAsset == null ? string.Empty : AssetDatabase.GetAssetPath(_sceneAsset));
        }
    }
}
