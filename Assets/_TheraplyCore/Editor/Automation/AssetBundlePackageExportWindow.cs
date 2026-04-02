using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public sealed class AssetBundlePackageExportWindow : EditorWindow
    {
        private const string MenuPath = "Theraply/Session Flow/Export/Build Asset Bundle Package";
        private const string PrefKeyPrefix = "Theraply.AssetBundleExport.";

        private string _gameId = "demo_cube_clicker";
        private string _contentVersion = "1.2.0";
        private string _basePackageUrl = "https://pranasense.pl/content";
        private string _outputDirectory = string.Empty;
        private BuildTarget _buildTarget = BuildTarget.Android;
        private SceneAsset _sceneAsset;
        private string _gameDefinitionPath = string.Empty;
        private string _mobileControlSchemaPath = string.Empty;
        private string _mobileControlLayoutPath = string.Empty;
        private string _notes = string.Empty;
        private bool _isBuilding;
        private string _status = "Ready";
        private string _lastManifestPath = string.Empty;
        private string _lastBundlePath = string.Empty;
        private string _lastPackageUri = string.Empty;
        private string _lastBundleUri = string.Empty;
        private Vector2 _scroll;

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            var window = GetWindow<AssetBundlePackageExportWindow>("Asset Bundle Export");
            window.minSize = new Vector2(780f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            EnsureDefaultOutputDirectory();
            if (_sceneAsset == null)
            {
                TryUseActiveScene();
            }
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Asset Bundle Package Export", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds unity scene asset bundle + .pkg.json manifest for server upload. " +
                "You can use the currently open scene or any selected scene asset.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_isBuilding))
            {
                DrawSceneSelection();
                EditorGUILayout.Space(8f);
                DrawBuildFields();
                EditorGUILayout.Space(8f);
                DrawContractFields();
                EditorGUILayout.Space(8f);
                DrawActionButtons();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(_status, MessageType.None);
            DrawLastBuildOutput();
        }

        private void DrawSceneSelection()
        {
            EditorGUILayout.LabelField("Scene", EditorStyles.boldLabel);

            var nextSceneAsset = EditorGUILayout.ObjectField(
                "Scene Asset",
                _sceneAsset,
                typeof(SceneAsset),
                false) as SceneAsset;
            if (nextSceneAsset != _sceneAsset)
            {
                _sceneAsset = nextSceneAsset;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Active Scene", GUILayout.Width(150f)))
            {
                TryUseActiveScene();
            }

            if (GUILayout.Button("Use Selected Scene", GUILayout.Width(150f)))
            {
                TryUseSelectedScene();
            }
            EditorGUILayout.EndHorizontal();

            var scenePath = GetSelectedScenePath();
            EditorGUILayout.LabelField(
                "Selected Scene Path",
                string.IsNullOrWhiteSpace(scenePath) ? "<none>" : scenePath);
        }

        private void DrawBuildFields()
        {
            EditorGUILayout.LabelField("Package Settings", EditorStyles.boldLabel);
            _gameId = EditorGUILayout.TextField("Game Id", _gameId ?? string.Empty);
            _contentVersion = EditorGUILayout.TextField("Content Version", _contentVersion ?? string.Empty);
            _basePackageUrl = EditorGUILayout.TextField("Base Package URL", _basePackageUrl ?? string.Empty);
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", _buildTarget);

            EditorGUILayout.BeginHorizontal();
            _outputDirectory = EditorGUILayout.TextField("Output Directory", _outputDirectory ?? string.Empty);
            if (GUILayout.Button("Browse", GUILayout.Width(72f)))
            {
                var selected = EditorUtility.OpenFolderPanel(
                    "Select output directory",
                    ResolveOutputDirectoryForDialog(),
                    string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _outputDirectory = selected.Trim();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawContractFields()
        {
            EditorGUILayout.LabelField("Optional Contract Paths", EditorStyles.boldLabel);
            _gameDefinitionPath = EditorGUILayout.TextField(
                "Game Definition JSON",
                _gameDefinitionPath ?? string.Empty);
            _mobileControlSchemaPath = EditorGUILayout.TextField(
                "Mobile Schema JSON",
                _mobileControlSchemaPath ?? string.Empty);
            _mobileControlLayoutPath = EditorGUILayout.TextField(
                "Mobile Layout JSON",
                _mobileControlLayoutPath ?? string.Empty);
            _notes = EditorGUILayout.TextField("Notes Override", _notes ?? string.Empty);

            if (GUILayout.Button("Auto-fill contracts from gameId", GUILayout.Width(220f)))
            {
                AutofillContractPaths();
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Settings", GUILayout.Width(120f)))
            {
                SavePrefs();
                _status = "Settings saved.";
            }

            if (GUILayout.Button("Build Bundle + Manifest", GUILayout.Width(190f)))
            {
                BuildPackage();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLastBuildOutput()
        {
            if (string.IsNullOrWhiteSpace(_lastManifestPath) &&
                string.IsNullOrWhiteSpace(_lastBundlePath))
            {
                return;
            }

            EditorGUILayout.LabelField("Last Build Output", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(140f));
            EditorGUILayout.TextField("Manifest Path", _lastManifestPath ?? string.Empty);
            EditorGUILayout.TextField("Bundle Path", _lastBundlePath ?? string.Empty);
            EditorGUILayout.TextField("Package URI", _lastPackageUri ?? string.Empty);
            EditorGUILayout.TextField("Bundle URI", _lastBundleUri ?? string.Empty);
            EditorGUILayout.EndScrollView();
        }

        private void BuildPackage()
        {
            SavePrefs();
            _isBuilding = true;
            try
            {
                var scenePath = GetSelectedScenePath();
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    throw new InvalidOperationException(
                        "Select scene first (Scene Asset or Use Active Scene/Use Selected Scene).");
                }

                var outputDirectory = (_outputDirectory ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    throw new InvalidOperationException("Output directory cannot be empty.");
                }

                var request = new AssetBundlePackageBuilder.EditorBuildRequest
                {
                    gameId = (_gameId ?? string.Empty).Trim(),
                    contentVersion = (_contentVersion ?? string.Empty).Trim(),
                    sceneAssetPath = scenePath,
                    outputDirectory = outputDirectory,
                    basePackageUrl = (_basePackageUrl ?? string.Empty).Trim(),
                    buildTarget = _buildTarget.ToString(),
                    gameDefinitionPath = ResolveOptionalContractPath(_gameDefinitionPath),
                    mobileControlSchemaPath = ResolveOptionalContractPath(_mobileControlSchemaPath),
                    mobileControlLayoutPath = ResolveOptionalContractPath(_mobileControlLayoutPath),
                    notes = (_notes ?? string.Empty).Trim(),
                };

                var result = AssetBundlePackageBuilder.BuildPackageFromEditor(request);
                _lastManifestPath = result.manifestPath ?? string.Empty;
                _lastBundlePath = result.bundlePath ?? string.Empty;
                _lastPackageUri = result.packageUri ?? string.Empty;
                _lastBundleUri = result.bundleUri ?? string.Empty;

                _status =
                    "PASS: bundle + manifest generated. Ready for upload to hosting/public/content or FTP target.";
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Asset Bundle Export",
                    "Build completed.\nManifest:\n" + _lastManifestPath + "\n\nBundle:\n" + _lastBundlePath,
                    "OK");
            }
            catch (Exception exception)
            {
                _status = "FAIL: " + exception.Message;
                Debug.LogError("[AssetBundlePackageExportWindow] " + exception);
                EditorUtility.DisplayDialog(
                    "Asset Bundle Export",
                    "Build failed:\n" + exception.Message,
                    "OK");
            }
            finally
            {
                _isBuilding = false;
                Repaint();
            }
        }

        private void TryUseActiveScene()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrWhiteSpace(activeScene.path))
            {
                _status = "Active scene has no saved asset path. Save scene first.";
                return;
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(activeScene.path);
            if (sceneAsset == null)
            {
                _status = "Could not load active scene asset: " + activeScene.path;
                return;
            }

            _sceneAsset = sceneAsset;
            _status = "Using active scene: " + activeScene.path;
        }

        private void TryUseSelectedScene()
        {
            var selectedScene = Selection.activeObject as SceneAsset;
            if (selectedScene == null)
            {
                _status = "Select a Scene asset in Project view first.";
                return;
            }

            _sceneAsset = selectedScene;
            _status = "Using selected scene: " + AssetDatabase.GetAssetPath(_sceneAsset);
        }

        private string GetSelectedScenePath()
        {
            return _sceneAsset == null ? string.Empty : AssetDatabase.GetAssetPath(_sceneAsset).Trim();
        }

        private void AutofillContractPaths()
        {
            var normalizedGameId = (_gameId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedGameId))
            {
                _status = "Set Game Id before auto-fill.";
                return;
            }

            var repoRoot = ResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                _status = "Could not resolve repo root for auto-fill.";
                return;
            }

            _gameDefinitionPath = TryResolveRepoContractPath(
                repoRoot,
                "contracts",
                "game_definition_" + normalizedGameId + ".json");
            _mobileControlSchemaPath = TryResolveRepoContractPath(
                repoRoot,
                "contracts",
                "mobile_control_schema_" + normalizedGameId + ".json");
            _mobileControlLayoutPath = TryResolveRepoContractPath(
                repoRoot,
                "contracts",
                "mobile_control_layout_" + normalizedGameId + ".json");
            _status = "Auto-fill completed (only existing files were populated).";
        }

        private static string TryResolveRepoContractPath(string repoRoot, params string[] segments)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || segments == null || segments.Length == 0)
            {
                return string.Empty;
            }

            var candidate = repoRoot;
            for (var i = 0; i < segments.Length; i++)
            {
                candidate = Path.Combine(candidate, segments[i]);
            }

            return File.Exists(candidate) ? candidate : string.Empty;
        }

        private string ResolveOptionalContractPath(string rawPath)
        {
            var value = (rawPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(value))
            {
                return Path.GetFullPath(value);
            }

            var repoRoot = ResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return value;
            }

            return Path.GetFullPath(Path.Combine(repoRoot, value));
        }

        private void EnsureDefaultOutputDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_outputDirectory))
            {
                return;
            }

            var repoRoot = ResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return;
            }

            _outputDirectory = Path.Combine(repoRoot, "hosting", "public", "content");
        }

        private string ResolveOutputDirectoryForDialog()
        {
            var output = (_outputDirectory ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output;
            }

            var repoRoot = ResolveRepoRoot();
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                return repoRoot;
            }

            return Directory.GetCurrentDirectory();
        }

        private static string ResolveRepoRoot()
        {
            var unityProjectRoot = Directory.GetParent(Application.dataPath);
            if (unityProjectRoot == null)
            {
                return string.Empty;
            }

            var repoRoot = unityProjectRoot.Parent;
            return repoRoot == null ? string.Empty : repoRoot.FullName;
        }

        private void LoadPrefs()
        {
            _gameId = EditorPrefs.GetString(PrefKeyPrefix + "GameId", _gameId);
            _contentVersion = EditorPrefs.GetString(PrefKeyPrefix + "ContentVersion", _contentVersion);
            _basePackageUrl = EditorPrefs.GetString(PrefKeyPrefix + "BasePackageUrl", _basePackageUrl);
            _outputDirectory = EditorPrefs.GetString(PrefKeyPrefix + "OutputDirectory", _outputDirectory);
            _gameDefinitionPath = EditorPrefs.GetString(PrefKeyPrefix + "GameDefinitionPath", _gameDefinitionPath);
            _mobileControlSchemaPath = EditorPrefs.GetString(PrefKeyPrefix + "MobileSchemaPath", _mobileControlSchemaPath);
            _mobileControlLayoutPath = EditorPrefs.GetString(PrefKeyPrefix + "MobileLayoutPath", _mobileControlLayoutPath);
            _notes = EditorPrefs.GetString(PrefKeyPrefix + "Notes", _notes);
            var buildTargetName = EditorPrefs.GetString(PrefKeyPrefix + "BuildTarget", _buildTarget.ToString());
            if (Enum.TryParse(buildTargetName, true, out BuildTarget parsed))
            {
                _buildTarget = parsed;
            }

            var scenePath = EditorPrefs.GetString(PrefKeyPrefix + "ScenePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                _sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            }
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefKeyPrefix + "GameId", (_gameId ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "ContentVersion", (_contentVersion ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "BasePackageUrl", (_basePackageUrl ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "OutputDirectory", (_outputDirectory ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "BuildTarget", _buildTarget.ToString());
            EditorPrefs.SetString(PrefKeyPrefix + "GameDefinitionPath", (_gameDefinitionPath ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "MobileSchemaPath", (_mobileControlSchemaPath ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "MobileLayoutPath", (_mobileControlLayoutPath ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "Notes", (_notes ?? string.Empty).Trim());
            EditorPrefs.SetString(PrefKeyPrefix + "ScenePath", GetSelectedScenePath());
        }
    }
}
