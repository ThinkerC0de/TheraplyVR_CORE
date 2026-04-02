using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace TheraplyCore.Editor.Authoring
{
    public sealed class FirebaseCatalogSyncWindow : EditorWindow
    {
        private const string MenuPath = "Theraply/Session Flow/Export/Sync Firebase";
        private const string ProjectIdPrefsKey = "Theraply.FirebaseCatalogSync.ProjectId";
        private const string IncludeExportPrefsKey = "Theraply.FirebaseCatalogSync.IncludeExport";
        private const string DeleteMissingPrefsKey = "Theraply.FirebaseCatalogSync.DeleteMissing";
        private const string DefaultProjectId = "theraply-vr-demo";

        private string _projectId = DefaultProjectId;
        private bool _includeExport = true;
        private bool _deleteMissing;
        private bool _isRunning;
        private Vector2 _logScroll;
        private string _lastLog = string.Empty;
        private string _status = "Ready";

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            var window = GetWindow<FirebaseCatalogSyncWindow>("Sync Firebase");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            _projectId = EditorPrefs.GetString(
                ProjectIdPrefsKey,
                Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") ?? DefaultProjectId);
            _includeExport = EditorPrefs.GetBool(IncludeExportPrefsKey, true);
            _deleteMissing = EditorPrefs.GetBool(DeleteMissingPrefsKey, false);
            _status = "Ready";
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Firebase Catalog Sync", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "One click sync for mobile catalog: export contracts, sync catalog seed files, and upsert Firestore game_catalog docs.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_isRunning))
            {
                _projectId = EditorGUILayout.TextField("Firebase Project ID", _projectId ?? string.Empty);
                _includeExport = EditorGUILayout.ToggleLeft("Export contracts before sync", _includeExport);
                _deleteMissing = EditorGUILayout.ToggleLeft("Delete stale docs not present in seed", _deleteMissing);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save Settings", GUILayout.Width(120f)))
                {
                    SavePrefs();
                }

                if (GUILayout.Button("Dry Run", GUILayout.Width(120f)))
                {
                    ExecuteSync(dryRun: true);
                }

                if (GUILayout.Button("Sync Firebase", GUILayout.Width(140f)))
                {
                    ExecuteSync(dryRun: false);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Status: " + _status);
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Execution Log", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
            EditorGUILayout.TextArea(string.IsNullOrWhiteSpace(_lastLog) ? "No sync run yet." : _lastLog, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(ProjectIdPrefsKey, (_projectId ?? string.Empty).Trim());
            EditorPrefs.SetBool(IncludeExportPrefsKey, _includeExport);
            EditorPrefs.SetBool(DeleteMissingPrefsKey, _deleteMissing);
        }

        private void ExecuteSync(bool dryRun)
        {
            SavePrefs();
            _isRunning = true;

            var log = new StringBuilder(1024);
            try
            {
                var projectId = (_projectId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    throw new InvalidOperationException("Firebase project id is required.");
                }

                var projectPath = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    throw new InvalidOperationException("Could not resolve Unity project path.");
                }

                var repoRoot = Directory.GetParent(projectPath)?.FullName;
                if (string.IsNullOrWhiteSpace(repoRoot))
                {
                    throw new InvalidOperationException("Could not resolve repository root path.");
                }

                EditorUtility.DisplayProgressBar("Sync Firebase", "Preparing...", 0.05f);
                log.AppendLine("[INFO] Repo root: " + repoRoot);
                log.AppendLine("[INFO] Firebase project: " + projectId);
                log.AppendLine("[INFO] Mode: " + (dryRun ? "DRY-RUN" : "LIVE"));

                if (_includeExport)
                {
                    EditorUtility.DisplayProgressBar("Sync Firebase", "Exporting contracts...", 0.20f);
                    var exportSummary = SessionFlowAuthoringExport.ExportAllToContracts();
                    log.AppendLine("[PASS] Exported contracts: " + exportSummary);

                    EditorUtility.DisplayProgressBar("Sync Firebase", "Syncing catalog schema files...", 0.38f);
                    RunPowerShellScript(
                        repoRoot,
                        Path.Combine(repoRoot, "scripts", "sync_mobile_control_schemas_to_catalog.ps1"),
                        "-RepoRoot " + QuoteArg(repoRoot),
                        log);

                    EditorUtility.DisplayProgressBar("Sync Firebase", "Syncing admin seed assets...", 0.52f);
                    RunPowerShellScript(
                        repoRoot,
                        Path.Combine(repoRoot, "scripts", "sync_admin_console_seed_assets.ps1"),
                        "-RepoRoot " + QuoteArg(repoRoot),
                        log);
                }

                EditorUtility.DisplayProgressBar("Sync Firebase", "Uploading game catalog to Firestore...", 0.74f);
                RunNodeSync(repoRoot, projectId, dryRun, _deleteMissing, log);

                EditorUtility.DisplayProgressBar("Sync Firebase", "Finalizing...", 0.95f);
                AssetDatabase.Refresh();

                _status = "PASS";
                _lastLog = log.ToString();
                UnityEngine.Debug.Log("[FirebaseCatalogSync] PASS\n" + _lastLog);
                EditorUtility.DisplayDialog("Sync Firebase", "Catalog sync completed successfully.", "OK");
            }
            catch (Exception exception)
            {
                log.AppendLine("[FAIL] " + exception.Message);
                _status = "FAIL";
                _lastLog = log.ToString();
                UnityEngine.Debug.LogError("[FirebaseCatalogSync] FAIL\n" + _lastLog + "\n" + exception);
                EditorUtility.DisplayDialog("Sync Firebase", "Catalog sync failed.\n" + exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isRunning = false;
                Repaint();
            }
        }

        private static void RunNodeSync(
            string repoRoot,
            string projectId,
            bool dryRun,
            bool deleteMissing,
            StringBuilder log)
        {
            var scriptPath = Path.Combine(repoRoot, "scripts", "sync_game_catalog_to_firebase.js");
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("Node sync script not found.", scriptPath);
            }

            var args = new StringBuilder();
            args.Append(QuoteArg(scriptPath));
            args.Append(" --repo-root ").Append(QuoteArg(repoRoot));
            args.Append(" --project-id ").Append(QuoteArg(projectId));
            if (dryRun)
            {
                args.Append(" --dry-run");
            }

            if (deleteMissing)
            {
                args.Append(" --delete-missing");
            }

            RunProcess("node", args.ToString(), repoRoot, log);
        }

        private static void RunPowerShellScript(
            string repoRoot,
            string scriptPath,
            string scriptArgs,
            StringBuilder log)
        {
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("PowerShell script not found.", scriptPath);
            }

            var args = "-NoProfile -ExecutionPolicy Bypass -File " +
                       QuoteArg(scriptPath) +
                       " " + scriptArgs;

            RunProcess("powershell", args, repoRoot, log);
        }

        private static void RunProcess(string fileName, string arguments, string workingDirectory, StringBuilder log)
        {
            var processStartInfo = new ProcessStartInfo
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
                process.StartInfo = processStartInfo;

                var stdOut = new StringBuilder(256);
                var stdErr = new StringBuilder(256);

                process.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        stdOut.AppendLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        stdErr.AppendLine(args.Data);
                    }
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start process: " + fileName);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                var stdout = stdOut.ToString();
                var stderr = stdErr.ToString();
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    log.AppendLine(stdout.TrimEnd());
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    log.AppendLine(stderr.TrimEnd());
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "Command failed (" + process.ExitCode + "): " +
                        fileName + " " + arguments);
                }
            }
        }

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
