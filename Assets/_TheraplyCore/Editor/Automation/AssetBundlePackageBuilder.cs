using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class AssetBundlePackageBuilder
    {
        public sealed class EditorBuildRequest
        {
            public string gameId = string.Empty;
            public string contentVersion = "1.0.0";
            public string sceneAssetPath = string.Empty;
            public string outputDirectory = string.Empty;
            public string basePackageUrl = string.Empty;
            public string manifestFileName = string.Empty;
            public string bundleFileName = string.Empty;
            public string buildTarget = "Android";
            public string gameDefinitionPath = string.Empty;
            public string mobileControlSchemaPath = string.Empty;
            public string mobileControlLayoutPath = string.Empty;
            public string notes = string.Empty;
        }

        public sealed class EditorBuildResult
        {
            public string gameId = string.Empty;
            public string contentVersion = string.Empty;
            public string sceneAssetPath = string.Empty;
            public string outputDirectory = string.Empty;
            public string manifestPath = string.Empty;
            public string bundlePath = string.Empty;
            public string packageUri = string.Empty;
            public string bundleUri = string.Empty;
        }

        [Serializable]
        private sealed class SourceContractRecord
        {
            public string path;
            public string sha256;
            public long bytes;
        }

        [Serializable]
        private sealed class SourceContractRecordList
        {
            public List<SourceContractRecord> items = new List<SourceContractRecord>();
        }

        [Serializable]
        private sealed class AssetBundleDescriptor
        {
            public string bundleUri;
            public string bundleFileName;
            public string bundleSha256;
            public long bundleBytes;
            public string unityBuildTarget;
            public string compression;
            public string sceneAssetPath;
            public string sceneName;
            public string loadMode;
        }

        [Serializable]
        private sealed class PackageManifestRecord
        {
            public string schema;
            public string schemaVersion;
            public string packageId;
            public string contentVersion;
            public string artifactType;
            public string deliveryMode;
            public string generatedAtUtc;
            public string packageUri;
            public AssetBundleDescriptor assetBundle;
            public string gameDefinitionPath;
            public string mobileControlSchemaPath;
            public string mobileControlLayoutPath;
            public SourceContractRecordList sourceContracts;
            public string notes;
        }

        private sealed class BuildArguments
        {
            public string gameId = string.Empty;
            public string contentVersion = "1.0.0";
            public string sceneAssetPath = string.Empty;
            public string outputDirectory = string.Empty;
            public string basePackageUrl = string.Empty;
            public string manifestFileName = string.Empty;
            public string bundleFileName = string.Empty;
            public string buildTarget = "Android";
            public string gameDefinitionPath = string.Empty;
            public string mobileControlSchemaPath = string.Empty;
            public string mobileControlLayoutPath = string.Empty;
            public string notes = string.Empty;
        }

        public static void BuildPackageFromCommandLine()
        {
            var args = ParseArgumentsFromCommandLine();
            BuildPackage(args);
        }

        public static EditorBuildResult BuildPackageFromEditor(EditorBuildRequest request)
        {
            if (request == null)
            {
                throw new InvalidOperationException("Editor build request is null.");
            }

            var args = new BuildArguments
            {
                gameId = request.gameId,
                contentVersion = request.contentVersion,
                sceneAssetPath = request.sceneAssetPath,
                outputDirectory = request.outputDirectory,
                basePackageUrl = request.basePackageUrl,
                manifestFileName = request.manifestFileName,
                bundleFileName = request.bundleFileName,
                buildTarget = request.buildTarget,
                gameDefinitionPath = request.gameDefinitionPath,
                mobileControlSchemaPath = request.mobileControlSchemaPath,
                mobileControlLayoutPath = request.mobileControlLayoutPath,
                notes = request.notes,
            };

            return BuildPackage(args);
        }

        private static EditorBuildResult BuildPackage(BuildArguments args)
        {
            ValidateRequiredInputs(args);

            var outputDirectory = Path.GetFullPath(args.outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            var normalizedVersionToken = BuildVersionToken(args.contentVersion);
            var bundleFileName = string.IsNullOrWhiteSpace(args.bundleFileName)
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}_android.bundle",
                    args.gameId,
                    normalizedVersionToken)
                : args.bundleFileName.Trim();
            var manifestFileName = string.IsNullOrWhiteSpace(args.manifestFileName)
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}.pkg.json",
                    args.gameId,
                    normalizedVersionToken)
                : args.manifestFileName.Trim();

            var buildTarget = ParseBuildTarget(args.buildTarget);
            var tempBuildDirectory = Path.Combine(
                Path.GetTempPath(),
                "TheraplyPackageBuild",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}_{2}",
                    args.gameId,
                    normalizedVersionToken,
                    buildTarget));

            if (Directory.Exists(tempBuildDirectory))
            {
                Directory.Delete(tempBuildDirectory, true);
            }

            Directory.CreateDirectory(tempBuildDirectory);

            var assetBundleBuild = new AssetBundleBuild
            {
                assetBundleName = bundleFileName,
                assetNames = new[] { args.sceneAssetPath.Trim() },
            };

            var buildManifest = BuildPipeline.BuildAssetBundles(
                tempBuildDirectory,
                new[] { assetBundleBuild },
                BuildAssetBundleOptions.ChunkBasedCompression,
                buildTarget);

            if (buildManifest == null)
            {
                throw new InvalidOperationException("BuildAssetBundles returned null manifest.");
            }

            var builtBundlePath = Path.Combine(tempBuildDirectory, bundleFileName);
            if (!File.Exists(builtBundlePath))
            {
                throw new InvalidOperationException("Built bundle file not found: " + builtBundlePath);
            }

            var outputBundlePath = Path.Combine(outputDirectory, bundleFileName);
            File.Copy(builtBundlePath, outputBundlePath, true);
            var bundleBytes = File.ReadAllBytes(outputBundlePath);
            var bundleSha = ComputeSha256Hex(bundleBytes);
            var bundleLength = new FileInfo(outputBundlePath).Length;

            var sourceRecords = BuildSourceContractRecords(
                args.gameDefinitionPath,
                args.mobileControlSchemaPath,
                args.mobileControlLayoutPath);

            var baseUrl = NormalizeBaseUrl(args.basePackageUrl);
            var packageUri = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}",
                baseUrl,
                manifestFileName);
            var bundleUri = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}",
                baseUrl,
                bundleFileName);

            var manifest = new PackageManifestRecord
            {
                schema = "THERAPLY_ASSET_BUNDLE_GAME_PACKAGE",
                schemaVersion = "2026-03-03",
                packageId = args.gameId.Trim(),
                contentVersion = args.contentVersion.Trim(),
                artifactType = "unity_scene_asset_bundle",
                deliveryMode = "on_demand",
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                packageUri = packageUri,
                assetBundle = new AssetBundleDescriptor
                {
                    bundleUri = bundleUri,
                    bundleFileName = bundleFileName,
                    bundleSha256 = bundleSha,
                    bundleBytes = bundleLength,
                    unityBuildTarget = buildTarget.ToString(),
                    compression = "chunk",
                    sceneAssetPath = args.sceneAssetPath.Trim(),
                    sceneName = Path.GetFileNameWithoutExtension(args.sceneAssetPath.Trim()),
                    loadMode = "additive",
                },
                gameDefinitionPath = NormalizeOptionalPath(args.gameDefinitionPath),
                mobileControlSchemaPath = NormalizeOptionalPath(args.mobileControlSchemaPath),
                mobileControlLayoutPath = NormalizeOptionalPath(args.mobileControlLayoutPath),
                sourceContracts = new SourceContractRecordList { items = sourceRecords },
                notes = string.IsNullOrWhiteSpace(args.notes)
                    ? "Install downloads manifest + asset bundle; START loads scene additively from persistent storage."
                    : args.notes.Trim(),
            };

            var outputManifestPath = Path.Combine(outputDirectory, manifestFileName);
            WriteJsonUtf8NoBom(outputManifestPath, JsonUtility.ToJson(manifest, true));

            Debug.Log(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[AssetBundlePackageBuilder] packageId={0} contentVersion={1} bundle={2} manifest={3}",
                    manifest.packageId,
                    manifest.contentVersion,
                    outputBundlePath,
                    outputManifestPath));

            return new EditorBuildResult
            {
                gameId = manifest.packageId,
                contentVersion = manifest.contentVersion,
                sceneAssetPath = args.sceneAssetPath.Trim(),
                outputDirectory = outputDirectory,
                manifestPath = outputManifestPath,
                bundlePath = outputBundlePath,
                packageUri = packageUri,
                bundleUri = bundleUri,
            };
        }

        private static BuildArguments ParseArgumentsFromCommandLine()
        {
            var parsed = new BuildArguments();
            var commandLineArgs = Environment.GetCommandLineArgs();
            for (var i = 0; i < commandLineArgs.Length; i++)
            {
                var current = commandLineArgs[i];
                if (string.IsNullOrWhiteSpace(current) || !current.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                var hasValue = i + 1 < commandLineArgs.Length &&
                               !commandLineArgs[i + 1].StartsWith("-", StringComparison.Ordinal);
                var value = hasValue ? commandLineArgs[i + 1] : string.Empty;
                if (hasValue)
                {
                    i++;
                }

                switch (current)
                {
                    case "-packageGameId":
                        parsed.gameId = value;
                        break;
                    case "-packageVersion":
                        parsed.contentVersion = value;
                        break;
                    case "-packageScenePath":
                        parsed.sceneAssetPath = value;
                        break;
                    case "-packageOutputDirectory":
                        parsed.outputDirectory = value;
                        break;
                    case "-packageBaseUrl":
                        parsed.basePackageUrl = value;
                        break;
                    case "-packageManifestFileName":
                        parsed.manifestFileName = value;
                        break;
                    case "-packageBundleFileName":
                        parsed.bundleFileName = value;
                        break;
                    case "-packageBuildTarget":
                        parsed.buildTarget = value;
                        break;
                    case "-packageGameDefinitionPath":
                        parsed.gameDefinitionPath = value;
                        break;
                    case "-packageMobileSchemaPath":
                        parsed.mobileControlSchemaPath = value;
                        break;
                    case "-packageMobileLayoutPath":
                        parsed.mobileControlLayoutPath = value;
                        break;
                    case "-packageNotes":
                        parsed.notes = value;
                        break;
                }
            }

            return parsed;
        }

        private static void ValidateRequiredInputs(BuildArguments args)
        {
            if (args == null)
            {
                throw new InvalidOperationException("Build arguments are null.");
            }

            if (string.IsNullOrWhiteSpace(args.gameId))
            {
                throw new InvalidOperationException("Missing required argument: -packageGameId");
            }

            if (string.IsNullOrWhiteSpace(args.contentVersion))
            {
                throw new InvalidOperationException("Missing required argument: -packageVersion");
            }

            if (string.IsNullOrWhiteSpace(args.sceneAssetPath))
            {
                throw new InvalidOperationException("Missing required argument: -packageScenePath");
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(args.sceneAssetPath.Trim());
            if (sceneAsset == null)
            {
                throw new InvalidOperationException("Scene asset does not exist: " + args.sceneAssetPath);
            }

            if (string.IsNullOrWhiteSpace(args.outputDirectory))
            {
                throw new InvalidOperationException("Missing required argument: -packageOutputDirectory");
            }

            if (string.IsNullOrWhiteSpace(args.basePackageUrl))
            {
                throw new InvalidOperationException("Missing required argument: -packageBaseUrl");
            }
        }

        private static BuildTarget ParseBuildTarget(string buildTargetRaw)
        {
            if (string.IsNullOrWhiteSpace(buildTargetRaw))
            {
                return BuildTarget.Android;
            }

            if (Enum.TryParse(buildTargetRaw.Trim(), true, out BuildTarget parsed))
            {
                return parsed;
            }

            return BuildTarget.Android;
        }

        private static string BuildVersionToken(string version)
        {
            var normalized = string.IsNullOrWhiteSpace(version)
                ? "1_0_0"
                : version.Trim();
            var builder = new StringBuilder(normalized.Length);
            for (var i = 0; i < normalized.Length; i++)
            {
                var current = normalized[i];
                builder.Append(char.IsLetterOrDigit(current) ? current : '_');
            }

            var token = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(token) ? "1_0_0" : token.ToLowerInvariant();
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            var trimmed = string.IsNullOrWhiteSpace(baseUrl)
                ? string.Empty
                : baseUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new InvalidOperationException("Base package URL is empty.");
            }

            return trimmed;
        }

        private static string NormalizeOptionalPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Replace("\\", "/");
        }

        private static List<SourceContractRecord> BuildSourceContractRecords(
            string gameDefinitionPath,
            string mobileSchemaPath,
            string mobileLayoutPath)
        {
            var records = new List<SourceContractRecord>();
            TryAddSourceContract(records, gameDefinitionPath);
            TryAddSourceContract(records, mobileSchemaPath);
            TryAddSourceContract(records, mobileLayoutPath);
            return records;
        }

        private static void TryAddSourceContract(List<SourceContractRecord> records, string sourcePath)
        {
            if (records == null || string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            var trimmed = sourcePath.Trim();
            if (!File.Exists(trimmed))
            {
                return;
            }

            var bytes = File.ReadAllBytes(trimmed);
            records.Add(new SourceContractRecord
            {
                path = NormalizeOptionalPath(trimmed),
                sha256 = ComputeSha256Hex(bytes),
                bytes = bytes.LongLength,
            });
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        private static void WriteJsonUtf8NoBom(string outputPath, string json)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                outputPath,
                (json ?? string.Empty) + Environment.NewLine,
                new UTF8Encoding(false));
        }
    }
}

