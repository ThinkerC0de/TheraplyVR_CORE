using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheraplyCore.Games.Contracts;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace TheraplyCore.Editor.Authoring
{
    /// <summary>
    /// Visual editor for contracts/game_catalog_seed.json.
    ///
    /// Allows adding, editing, and removing catalog entries without touching JSON directly.
    /// "mobileControlSchema" and "mobileControlLayout" embedded objects are NOT managed here —
    /// they are re-injected by sync_mobile_control_schemas_to_catalog.ps1 on every save+sync.
    ///
    /// Workflow:
    ///   1. Add games from discovered GameDefinitionAssets (auto-fills gameId, title, version).
    ///   2. Fill in Package URI (or hit "Auto" to compute from Base Package URL + version).
    ///   3. Save Seed JSON → updates contracts/game_catalog_seed.json.
    ///   4. Save + Sync Firebase → saves + injects schemas via PS scripts + upserts to Firestore.
    /// </summary>
    public sealed class GameCatalogEditorWindow : EditorWindow
    {
        private const string MenuPath = "Theraply/Session Flow/Export/Game Catalog Editor";
        private const string PrefKeyPrefix = "Theraply.CatalogEditor.";
        private const string DefaultProjectId = "theraply-vr-demo";
        private const string DefaultBaseUrl = "https://pranasense.pl/content";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ── Settings ─────────────────────────────────────────────────────────
        private string _basePackageUrl = DefaultBaseUrl;
        private string _firebaseProjectId = DefaultProjectId;

        // ── Catalog state ─────────────────────────────────────────────────────
        private readonly List<CatalogEntryModel> _entries = new List<CatalogEntryModel>();
        private bool _isDirty;
        private string _statusMessage = string.Empty;

        // ── UI state ──────────────────────────────────────────────────────────
        private Vector2 _scroll;
        private readonly HashSet<int> _expandedIndices = new HashSet<int>();

        // ── Discovery state ───────────────────────────────────────────────────
        private string[] _allGameIds = new string[0];
        private string[] _availableToAdd = new string[0];
        private int _addGamePopupIndex;

        // ─── Serializable models ──────────────────────────────────────────────

        [Serializable]
        private sealed class CatalogEntryModel
        {
            public string gameId = string.Empty;
            public string title = string.Empty;
            public string description = string.Empty;
            public string targetContentVersion = "1.0.0";
            public string packageUri = string.Empty;
            public string thumbnailUrl = string.Empty;
            public bool supportsSaveResume;
            public bool availableForPurchase = true;
            public bool requiresExplicitLicense;
            public bool runtimeLaunchEnabled = true;
            public int sortOrder;
            public bool active = true;
            public List<string> previewLines = new List<string>();
        }

        /// <summary>
        /// Used only for JSON round-trip of the seed file header + entries.
        /// Complex embedded objects (mobileControlSchema, mobileControlLayout) are
        /// intentionally excluded — they are managed by the PS sync scripts.
        /// </summary>
        [Serializable]
        private sealed class CatalogSeedFile
        {
            public string schema = "THERAPLY_GAME_CATALOG";
            public string schemaVersion = "2026-02-25";
            public string version = string.Empty;
            public string collection = "game_catalog";
            public List<CatalogEntryModel> entries = new List<CatalogEntryModel>();
        }

        // ─── Menu ─────────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            var window = GetWindow<GameCatalogEditorWindow>("Catalog Editor");
            window.minSize = new Vector2(800f, 640f);
            window.Show();
        }

        // ─── Unity lifecycle ──────────────────────────────────────────────────

        private void OnEnable()
        {
            LoadPrefs();
            RefreshDiscovery();
            LoadFromDisk();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        // ─── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Game Catalog Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Manages contracts/game_catalog_seed.json — the source of truth for the mobile shop.\n" +
                "mobileControlSchema / mobileControlLayout are injected by PS scripts — no need to edit here.",
                MessageType.Info);

            DrawSettingsBar();
            EditorGUILayout.Space(4f);
            DrawAddBar();
            EditorGUILayout.Space(4f);
            DrawEntriesList();
            EditorGUILayout.Space(6f);
            DrawActionButtons();
        }

        private void DrawSettingsBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var newUrl = EditorGUILayout.TextField("Base Package URL", _basePackageUrl ?? string.Empty);
            if (newUrl != _basePackageUrl) _basePackageUrl = newUrl;

            var newId = EditorGUILayout.TextField("Firebase Project ID", _firebaseProjectId ?? string.Empty);
            if (newId != _firebaseProjectId) _firebaseProjectId = newId;

            if (!string.IsNullOrWhiteSpace(_statusMessage))
                EditorGUILayout.LabelField(_statusMessage, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawAddBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Games", EditorStyles.boldLabel, GUILayout.Width(50f));

            GUILayout.FlexibleSpace();

            if (_availableToAdd.Length > 0)
            {
                _addGamePopupIndex = EditorGUILayout.Popup(
                    _addGamePopupIndex, _availableToAdd, GUILayout.Width(220f));
                if (GUILayout.Button("+ Add Game", GUILayout.Width(100f)))
                    AddEntryFromDefinition(_availableToAdd[_addGamePopupIndex]);
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    GUILayout.Button("All project games are in catalog", GUILayout.Width(260f));
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70f)))
            {
                RefreshDiscovery();
                SetStatus("Refreshed.");
            }

            if (GUILayout.Button("Reload File", GUILayout.Width(90f)))
            {
                if (!_isDirty || EditorUtility.DisplayDialog(
                    "Reload", "Unsaved changes will be lost. Reload?", "Reload", "Cancel"))
                    LoadFromDisk();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntriesList()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            var removeAt = -1;

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var isExpanded = _expandedIndices.Contains(i);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // ── Header row ───────────────────────────────────────────────
                EditorGUILayout.BeginHorizontal();

                var newExpanded = EditorGUILayout.Foldout(
                    isExpanded,
                    entry.gameId + "   v" + entry.targetContentVersion +
                    (entry.active ? string.Empty : "  [INACTIVE]"),
                    true,
                    EditorStyles.foldoutHeader);
                if (newExpanded != isExpanded)
                {
                    if (newExpanded) _expandedIndices.Add(i);
                    else _expandedIndices.Remove(i);
                }

                var wasActive = entry.active;
                entry.active = GUILayout.Toggle(entry.active, "active", GUILayout.Width(56f));
                if (entry.active != wasActive) _isDirty = true;

                var wasLaunch = entry.runtimeLaunchEnabled;
                entry.runtimeLaunchEnabled = GUILayout.Toggle(
                    entry.runtimeLaunchEnabled, "launch", GUILayout.Width(56f));
                if (entry.runtimeLaunchEnabled != wasLaunch) _isDirty = true;

                var prevColor = GUI.color;
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    removeAt = i;
                GUI.color = prevColor;

                EditorGUILayout.EndHorizontal();

                // ── Expanded body ────────────────────────────────────────────
                if (newExpanded)
                {
                    EditorGUI.indentLevel++;

                    MarkDirtyIf(ref entry.title,
                        EditorGUILayout.TextField("Title", entry.title));
                    MarkDirtyIf(ref entry.description,
                        EditorGUILayout.TextField("Description", entry.description));

                    EditorGUILayout.BeginHorizontal();
                    MarkDirtyIf(ref entry.targetContentVersion,
                        EditorGUILayout.TextField("Version", entry.targetContentVersion));
                    var newSort = EditorGUILayout.IntField(
                        "Sort Order", entry.sortOrder, GUILayout.Width(160f));
                    if (newSort != entry.sortOrder)
                    {
                        entry.sortOrder = newSort;
                        _isDirty = true;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    MarkDirtyIf(ref entry.packageUri,
                        EditorGUILayout.TextField("Package URI", entry.packageUri));
                    if (GUILayout.Button("Auto", GUILayout.Width(48f)))
                    {
                        entry.packageUri = BuildPackageUri(entry.gameId, entry.targetContentVersion);
                        _isDirty = true;
                    }
                    EditorGUILayout.EndHorizontal();

                    MarkDirtyIf(ref entry.thumbnailUrl,
                        EditorGUILayout.TextField("Thumbnail URL", entry.thumbnailUrl));

                    EditorGUILayout.BeginHorizontal();
                    MarkDirtyToggle(ref entry.availableForPurchase,
                        EditorGUILayout.ToggleLeft("availableForPurchase",
                            entry.availableForPurchase, GUILayout.Width(170f)));
                    MarkDirtyToggle(ref entry.requiresExplicitLicense,
                        EditorGUILayout.ToggleLeft("requiresExplicitLicense",
                            entry.requiresExplicitLicense, GUILayout.Width(190f)));
                    MarkDirtyToggle(ref entry.supportsSaveResume,
                        EditorGUILayout.ToggleLeft("supportsSaveResume",
                            entry.supportsSaveResume, GUILayout.Width(160f)));
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.EndScrollView();

            if (removeAt >= 0)
            {
                _entries.RemoveAt(removeAt);
                _expandedIndices.Remove(removeAt);
                _isDirty = true;
                RefreshAvailableToAdd();
                SetStatus("Entry removed.");
            }
        }

        private void DrawActionButtons()
        {
            var dirtyMarker = _isDirty ? "  •" : string.Empty;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (GUILayout.Button("Save Seed JSON" + dirtyMarker, GUILayout.Height(28f)))
                SaveToDisk();

            if (GUILayout.Button("Save + Run PS Scripts", GUILayout.Height(28f)))
            {
                if (SaveToDisk())
                    RunPsScripts();
            }

            if (GUILayout.Button("Save + Sync Firebase", GUILayout.Height(28f)))
            {
                if (SaveToDisk())
                    if (RunPsScripts())
                        RunFirebaseSync();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── Helpers for dirty tracking ───────────────────────────────────────

        private void MarkDirtyIf(ref string field, string newValue)
        {
            if (newValue == field) return;
            field = newValue;
            _isDirty = true;
        }

        private void MarkDirtyToggle(ref bool field, bool newValue)
        {
            if (newValue == field) return;
            field = newValue;
            _isDirty = true;
        }

        // ─── Data operations ──────────────────────────────────────────────────

        private void LoadFromDisk()
        {
            var seedPath = ResolveSeedPath();
            _entries.Clear();
            _expandedIndices.Clear();

            if (!File.Exists(seedPath))
            {
                SetStatus("Seed file not found — will be created on Save: " + seedPath);
                _isDirty = false;
                RefreshAvailableToAdd();
                return;
            }

            try
            {
                var text = File.ReadAllText(seedPath, Encoding.UTF8);
                var seed = JsonUtility.FromJson<CatalogSeedFile>(text);
                if (seed?.entries != null)
                    _entries.AddRange(seed.entries);
                _isDirty = false;
                SetStatus("Loaded " + _entries.Count + " entr" + (_entries.Count == 1 ? "y" : "ies") + ".");
                RefreshAvailableToAdd();
            }
            catch (Exception ex)
            {
                SetStatus("Load failed: " + ex.Message);
                Debug.LogError("[GameCatalogEditor] Load failed: " + ex);
            }
        }

        /// <returns>true if save succeeded</returns>
        private bool SaveToDisk()
        {
            var seedPath = ResolveSeedPath();
            try
            {
                var seed = new CatalogSeedFile
                {
                    schema = "THERAPLY_GAME_CATALOG",
                    schemaVersion = "2026-02-25",
                    version = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    collection = "game_catalog",
                    entries = new List<CatalogEntryModel>(_entries),
                };

                var json = JsonUtility.ToJson(seed, true);
                var dir = Path.GetDirectoryName(seedPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(seedPath, json + Environment.NewLine, Utf8NoBom);
                _isDirty = false;
                AssetDatabase.Refresh();
                SetStatus("Saved " + _entries.Count + " entr" + (_entries.Count == 1 ? "y" : "ies") + " → " + seedPath);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message);
                Debug.LogError("[GameCatalogEditor] Save failed: " + ex);
                EditorUtility.DisplayDialog("Catalog Editor", "Save failed:\n" + ex.Message, "OK");
                return false;
            }
        }

        /// <returns>true if scripts ran successfully</returns>
        private bool RunPsScripts()
        {
            var repoRoot = ResolveRepoRoot();
            var log = new StringBuilder();
            try
            {
                EditorUtility.DisplayProgressBar("Catalog Editor", "Running PS scripts...", 0.3f);

                var script1 = Path.Combine(repoRoot, "scripts", "sync_mobile_control_schemas_to_catalog.ps1");
                if (File.Exists(script1))
                {
                    RunPowershell(script1, "-RepoRoot " + QuoteArg(repoRoot), repoRoot, log);
                    SetStatus("sync_mobile_control_schemas_to_catalog.ps1 — OK");
                }

                var script2 = Path.Combine(repoRoot, "scripts", "sync_admin_console_seed_assets.ps1");
                if (File.Exists(script2))
                {
                    RunPowershell(script2, "-RepoRoot " + QuoteArg(repoRoot), repoRoot, log);
                    SetStatus("sync_admin_console_seed_assets.ps1 — OK");
                }

                // Reload so editor reflects re-injected schema fields in status
                LoadFromDisk();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("PS scripts failed: " + ex.Message);
                Debug.LogError("[GameCatalogEditor] PS scripts: " + ex + "\n" + log);
                EditorUtility.DisplayDialog("Catalog Editor", "PS scripts failed:\n" + ex.Message, "OK");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <returns>true if sync succeeded</returns>
        private bool RunFirebaseSync()
        {
            var repoRoot = ResolveRepoRoot();
            var projectId = (_firebaseProjectId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(projectId))
            {
                EditorUtility.DisplayDialog("Catalog Editor", "Firebase Project ID is required.", "OK");
                return false;
            }

            var log = new StringBuilder();
            try
            {
                EditorUtility.DisplayProgressBar("Catalog Editor", "Syncing Firebase catalog...", 0.7f);

                var scriptPath = Path.Combine(repoRoot, "scripts", "sync_game_catalog_to_firebase.js");
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("Node sync script not found.", scriptPath);

                var args = new StringBuilder();
                args.Append(QuoteArg(scriptPath));
                args.Append(" --repo-root ").Append(QuoteArg(repoRoot));
                args.Append(" --project-id ").Append(QuoteArg(projectId));
                RunProcess("node", args.ToString(), repoRoot, log);

                SetStatus("Firebase synced OK — " + _entries.Count + " game(s) upserted.");
                EditorUtility.DisplayDialog("Catalog Editor",
                    "Firebase catalog synced successfully.\n" + _entries.Count + " game(s) upserted.", "OK");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Firebase sync failed: " + ex.Message);
                Debug.LogError("[GameCatalogEditor] Firebase sync: " + ex + "\n" + log);
                EditorUtility.DisplayDialog("Catalog Editor", "Firebase sync failed:\n" + ex.Message, "OK");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ─── Entry creation ───────────────────────────────────────────────────

        private void AddEntryFromDefinition(string gameId)
        {
            var asset = FindGameDefinitionAsset(gameId);
            var displayName = asset?.definition?.displayName;
            var title = string.IsNullOrWhiteSpace(displayName) ? gameId : displayName.Trim();
            var version = "1.0.0";

            var entry = new CatalogEntryModel
            {
                gameId = gameId,
                title = title,
                description = string.Empty,
                targetContentVersion = version,
                packageUri = BuildPackageUri(gameId, version),
                active = true,
                runtimeLaunchEnabled = true,
                availableForPurchase = true,
                sortOrder = _entries.Count * 10 + 10,
            };

            _entries.Add(entry);
            _expandedIndices.Add(_entries.Count - 1);
            _isDirty = true;
            RefreshAvailableToAdd();
            SetStatus("Added: " + gameId + " — fill in Version + Package URI, then Save.");
        }

        private string BuildPackageUri(string gameId, string version)
        {
            var baseUrl = (_basePackageUrl ?? string.Empty).TrimEnd('/');
            var versionToken = BuildVersionToken(version);
            return baseUrl + "/" + gameId + "_" + versionToken + ".pkg.json";
        }

        private static string BuildVersionToken(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return "1_0_0";
            var sb = new StringBuilder();
            foreach (var c in version.Trim())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            var token = sb.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(token) ? "1_0_0" : token.ToLowerInvariant();
        }

        // ─── Discovery ────────────────────────────────────────────────────────

        private void RefreshDiscovery()
        {
            var ids = new List<string>();
            var guids = AssetDatabase.FindAssets("t:GameDefinitionAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(path);
                if (asset?.definition != null &&
                    !string.IsNullOrWhiteSpace(asset.definition.gameId))
                {
                    var id = asset.definition.gameId.Trim();
                    if (!ids.Contains(id))
                        ids.Add(id);
                }
            }
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            _allGameIds = ids.ToArray();
            RefreshAvailableToAdd();
        }

        private void RefreshAvailableToAdd()
        {
            var inCatalog = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _entries)
                if (!string.IsNullOrWhiteSpace(e.gameId))
                    inCatalog.Add(e.gameId.Trim());

            var available = new List<string>();
            foreach (var id in _allGameIds)
                if (!inCatalog.Contains(id))
                    available.Add(id);

            _availableToAdd = available.ToArray();
            if (_addGamePopupIndex >= _availableToAdd.Length)
                _addGamePopupIndex = 0;
        }

        private static GameDefinitionAsset FindGameDefinitionAsset(string gameId)
        {
            var guids = AssetDatabase.FindAssets("t:GameDefinitionAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(path);
                if (asset?.definition != null &&
                    string.Equals(
                        asset.definition.gameId?.Trim(), gameId,
                        StringComparison.OrdinalIgnoreCase))
                    return asset;
            }
            return null;
        }

        // ─── Path helpers ─────────────────────────────────────────────────────

        private static string ResolveSeedPath()
        {
            var repoRoot = ResolveRepoRoot();
            return string.IsNullOrWhiteSpace(repoRoot)
                ? string.Empty
                : Path.Combine(repoRoot, "contracts", "game_catalog_seed.json");
        }

        private static string ResolveRepoRoot()
        {
            var unityProjectRoot = Directory.GetParent(Application.dataPath);
            var repoRoot = unityProjectRoot?.Parent;
            return repoRoot == null ? string.Empty : repoRoot.FullName;
        }

        private void SetStatus(string message)
        {
            _statusMessage = message;
            Repaint();
        }

        // ─── Process helpers ──────────────────────────────────────────────────

        private static void RunPowershell(
            string scriptPath, string scriptArgs, string workDir, StringBuilder log)
        {
            var fullArgs = "-NoProfile -ExecutionPolicy Bypass -File " +
                           QuoteArg(scriptPath) + " " + scriptArgs;
            RunProcess("powershell", fullArgs, workDir, log);
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
                process.OutputDataReceived += (_, a) =>
                {
                    if (!string.IsNullOrEmpty(a.Data)) stdOut.AppendLine(a.Data);
                };
                process.ErrorDataReceived += (_, a) =>
                {
                    if (!string.IsNullOrEmpty(a.Data)) stdErr.AppendLine(a.Data);
                };

                if (!process.Start())
                    throw new InvalidOperationException("Failed to start: " + fileName);

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

        // ─── Prefs ────────────────────────────────────────────────────────────

        private void LoadPrefs()
        {
            _basePackageUrl = EditorPrefs.GetString(PrefKeyPrefix + "BasePackageUrl", DefaultBaseUrl);
            _firebaseProjectId = EditorPrefs.GetString(PrefKeyPrefix + "FirebaseProjectId", DefaultProjectId);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefKeyPrefix + "BasePackageUrl", (_basePackageUrl ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "FirebaseProjectId", (_firebaseProjectId ?? string.Empty).Trim());
        }
    }
}
