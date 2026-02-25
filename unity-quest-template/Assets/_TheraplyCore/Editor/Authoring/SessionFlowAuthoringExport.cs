using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TheraplyCore.Games.Contracts;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Authoring
{
    public static class SessionFlowAuthoringExport
    {
        private const string ManifestSchema = "THERAPLY_GAME_DEFINITION_EXPORT_MANIFEST";
        private const string ManifestSchemaVersion = "2026-02-25";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        [MenuItem("Theraply/Session Flow/Export/Export All To Contracts")]
        public static void ExportAllToContractsMenu()
        {
            try
            {
                var summary = ExecuteExport();
                Debug.Log("[SessionFlowAuthoringExport] PASS: " + summary);
                EditorUtility.DisplayDialog("Session Flow Export", "Export completed.\n" + summary, "OK");
            }
            catch (Exception exception)
            {
                Debug.LogError("[SessionFlowAuthoringExport] FAIL: " + exception);
                EditorUtility.DisplayDialog("Session Flow Export", "Export failed.\n" + exception.Message, "OK");
                throw;
            }
        }

        public static void ExportAllGameDefinitionsToContracts()
        {
            try
            {
                var summary = ExecuteExport();
                Debug.Log("[SessionFlowAuthoringExport] PASS: " + summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SessionFlowAuthoringExport] FAIL: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteExport()
        {
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

            var contractsDir = Path.Combine(repoRoot, "contracts");
            var gameDefinitionsDir = Path.Combine(contractsDir, "game_definitions");
            Directory.CreateDirectory(gameDefinitionsDir);

            var assetPaths = DiscoverGameDefinitionAssetPaths();
            if (assetPaths.Count == 0)
            {
                throw new InvalidOperationException("No GameDefinitionAsset found in project.");
            }

            var manifest = new GameDefinitionExportManifest
            {
                schema = ManifestSchema,
                schemaVersion = ManifestSchemaVersion,
                exportedAtUtc = DateTime.UtcNow.ToString("O"),
                entryCount = 0,
                entries = new List<GameDefinitionExportManifestEntry>(),
            };

            var exportedSchemas = 0;
            for (var i = 0; i < assetPaths.Count; i++)
            {
                var assetPath = assetPaths[i];
                var asset = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(assetPath);
                if (asset == null)
                {
                    throw new InvalidOperationException("Failed to load GameDefinitionAsset at path: " + assetPath);
                }

                var definition = Clone(asset.definition);
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        "GameDefinitionAsset has no valid definition payload: " + assetPath);
                }

                if (!SessionFlowDefinitionValidator.TryValidate(definition, out var definitionReason))
                {
                    throw new InvalidOperationException(
                        "Definition validation failed for '" + assetPath + "' reasonCode=" + definitionReason);
                }

                if (string.IsNullOrWhiteSpace(definition.gameId))
                {
                    throw new InvalidOperationException("Definition gameId is required: " + assetPath);
                }

                var safeGameId = SanitizeFileName(definition.gameId.Trim());
                if (string.IsNullOrWhiteSpace(safeGameId))
                {
                    throw new InvalidOperationException("Definition gameId could not be converted to file name: " + definition.gameId);
                }

                var definitionJsonPath = Path.Combine(gameDefinitionsDir, safeGameId + ".json");
                WriteJsonFile(definitionJsonPath, definition);

                var schemaJsonPathRelative = string.Empty;
                if (HasMobileControlSchemaPayload(definition.mobileControlSchema))
                {
                    if (!MobileControlSchemaValidator.TryValidate(definition.mobileControlSchema, out var schemaReason))
                    {
                        throw new InvalidOperationException(
                            "Mobile control schema validation failed for '" + assetPath + "' reasonCode=" + schemaReason);
                    }

                    var schemaJsonPath = Path.Combine(contractsDir, "mobile_control_schema_" + safeGameId + ".json");
                    WriteJsonFile(schemaJsonPath, definition.mobileControlSchema);
                    schemaJsonPathRelative = ToRepoRelativePath(repoRoot, schemaJsonPath);
                    exportedSchemas++;
                }

                manifest.entries.Add(new GameDefinitionExportManifestEntry
                {
                    gameId = definition.gameId.Trim(),
                    definitionAssetPath = assetPath,
                    definitionJsonPath = ToRepoRelativePath(repoRoot, definitionJsonPath),
                    mobileControlSchemaJsonPath = schemaJsonPathRelative,
                });
            }

            manifest.entryCount = manifest.entries.Count;
            var manifestPath = Path.Combine(contractsDir, "game_definition_export_manifest.json");
            WriteJsonFile(manifestPath, manifest);

            AssetDatabase.Refresh();

            return "definitions=" + manifest.entryCount +
                   "; schemas=" + exportedSchemas +
                   "; manifest=" + ToRepoRelativePath(repoRoot, manifestPath);
        }

        private static List<string> DiscoverGameDefinitionAssetPaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var directMatches = AssetDatabase.FindAssets("t:GameDefinitionAsset");
            for (var i = 0; i < directMatches.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(directMatches[i]);
                if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                {
                    paths.Add(path);
                }
            }

            if (paths.Count == 0)
            {
                var scriptableMatches = AssetDatabase.FindAssets("t:ScriptableObject");
                for (var i = 0; i < scriptableMatches.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(scriptableMatches[i]);
                    if (string.IsNullOrWhiteSpace(path) ||
                        !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                        !seen.Add(path))
                    {
                        continue;
                    }

                    var casted = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(path);
                    if (casted != null)
                    {
                        paths.Add(path);
                    }
                }
            }

            if (paths.Count == 0)
            {
                var dataPath = Application.dataPath.Replace('\\', '/');
                var assetFiles = Directory.GetFiles(Application.dataPath, "*.asset", SearchOption.AllDirectories);
                for (var i = 0; i < assetFiles.Length; i++)
                {
                    var fullPath = assetFiles[i].Replace('\\', '/');
                    if (!fullPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var relative = "Assets" + fullPath.Substring(dataPath.Length);
                    if (!seen.Add(relative))
                    {
                        continue;
                    }

                    var casted = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(relative);
                    if (casted != null)
                    {
                        paths.Add(relative);
                    }
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private static string ToRepoRelativePath(string repoRoot, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            var root = repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
            var rootUri = new Uri(root);
            var pathUri = new Uri(fullPath);
            var relative = rootUri.MakeRelativeUri(pathUri).ToString();
            return Uri.UnescapeDataString(relative).Replace('\\', '/');
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
            {
                if (invalidChars.Contains(c))
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(char.ToLowerInvariant(c));
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        private static void WriteJsonFile<T>(string path, T payload)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("JSON path is required.");
            }

            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var json = JsonUtility.ToJson(payload, true);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Could not serialize JSON payload for path: " + path);
            }

            File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
        }

        private static bool HasMobileControlSchemaPayload(MobileControlSchema schema)
        {
            if (schema == null)
            {
                return false;
            }

            var json = JsonUtility.ToJson(schema, false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            return !string.Equals(json.Trim(), "{}", StringComparison.Ordinal);
        }

        private static GameDefinition Clone(GameDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            var json = JsonUtility.ToJson(source);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<GameDefinition>(json);
        }
    }

    [Serializable]
    public sealed class GameDefinitionExportManifest
    {
        public string schema = string.Empty;
        public string schemaVersion = string.Empty;
        public string exportedAtUtc = string.Empty;
        public int entryCount = 0;
        public List<GameDefinitionExportManifestEntry> entries = new List<GameDefinitionExportManifestEntry>();
    }

    [Serializable]
    public sealed class GameDefinitionExportManifestEntry
    {
        public string gameId = string.Empty;
        public string definitionAssetPath = string.Empty;
        public string definitionJsonPath = string.Empty;
        public string mobileControlSchemaJsonPath = string.Empty;
    }
}
