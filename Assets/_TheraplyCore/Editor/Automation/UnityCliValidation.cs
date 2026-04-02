using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class UnityCliValidation
    {
        private const string BuildOutputEnvironmentVariable = "THERAPLY_UNITY_BUILD_OUTPUT";
        private const string BuildScenesEnvironmentVariable = "THERAPLY_UNITY_BUILD_SCENES";
        private const string DefaultBuildOutputRelativePath = "Temp/CliValidation/build/TheraplyCliValidation.apk";

        public static void RunCompileValidation()
        {
            ExecuteWithExitCode("compile", () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                if (EditorUtility.scriptCompilationFailed)
                {
                    throw new InvalidOperationException("Script compilation failed. See Unity compile log for details.");
                }

                Debug.Log("[CLI Validation] Script compilation passed.");
            });
        }

        public static void RunAndroidDebugBuildValidation()
        {
            ExecuteWithExitCode("android-build", () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                if (EditorUtility.scriptCompilationFailed)
                {
                    throw new InvalidOperationException("Script compilation failed before build.");
                }

                string[] scenes = ResolveBuildScenes();

                string outputPath = ResolveBuildOutputPath();
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development
                };

                Debug.Log($"[CLI Validation] Starting Android debug build. Output: {outputPath}");

                BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Build failed. Result={summary.result}; Errors={summary.totalErrors}; Warnings={summary.totalWarnings}");
                }

                Debug.Log(
                    $"[CLI Validation] Build succeeded. Duration={summary.totalTime.TotalSeconds:0.0}s; Size={summary.totalSize} bytes; Output={outputPath}");
            });
        }

        private static void ExecuteWithExitCode(string stepName, Action action)
        {
            try
            {
                Debug.Log($"[CLI Validation] Step '{stepName}' started.");
                action();
                Debug.Log($"[CLI Validation] Step '{stepName}' completed successfully.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[CLI Validation] Step '{stepName}' failed: {exception}");
                EditorApplication.Exit(1);
            }
        }

        private static string ResolveBuildOutputPath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(BuildOutputEnvironmentVariable);
            string relativeOrAbsolutePath = string.IsNullOrWhiteSpace(configuredPath)
                ? DefaultBuildOutputRelativePath
                : configuredPath;

            if (Path.IsPathRooted(relativeOrAbsolutePath))
            {
                return Path.GetFullPath(relativeOrAbsolutePath);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, relativeOrAbsolutePath));
        }

        private static string[] ResolveBuildScenes()
        {
            string[] enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (enabledScenes.Length > 0)
            {
                return enabledScenes;
            }

            string configuredScenesRaw = Environment.GetEnvironmentVariable(BuildScenesEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredScenesRaw))
            {
                string[] configuredScenes = configuredScenesRaw
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();

                if (configuredScenes.Length > 0)
                {
                    Debug.Log(
                        $"[CLI Validation] Build Settings had no enabled scenes. Using configured scene list from {BuildScenesEnvironmentVariable}: {string.Join(", ", configuredScenes)}");
                    return configuredScenes;
                }
            }

            string fallbackScene = "Assets/_Examples/Scenes/SessionResilienceTest.unity";
            if (File.Exists(ToAbsoluteProjectPath(fallbackScene)))
            {
                Debug.Log(
                    $"[CLI Validation] Build Settings had no enabled scenes. Falling back to default scene: {fallbackScene}");
                return new[] { fallbackScene };
            }

            string assetsRoot = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Assets");
            string[] discoveredScenes = Directory.Exists(assetsRoot)
                ? Directory.GetFiles(assetsRoot, "*.unity", SearchOption.AllDirectories)
                    .Select(ToAssetPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(1)
                    .ToArray()
                : Array.Empty<string>();

            if (discoveredScenes.Length > 0)
            {
                Debug.Log(
                    $"[CLI Validation] Build Settings had no enabled scenes. Falling back to first discovered scene: {discoveredScenes[0]}");
                return discoveredScenes;
            }

            throw new InvalidOperationException(
                "No scenes available for CLI build. Enable scenes in Build Settings or set THERAPLY_UNITY_BUILD_SCENES.");
        }

        private static string ToAbsoluteProjectPath(string relativeOrAbsolutePath)
        {
            if (Path.IsPathRooted(relativeOrAbsolutePath))
            {
                return Path.GetFullPath(relativeOrAbsolutePath);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, relativeOrAbsolutePath));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string normalizedAbsolute = absolutePath.Replace('\\', '/');
            string normalizedAssetsRoot = Application.dataPath.Replace('\\', '/');

            if (!normalizedAbsolute.StartsWith(normalizedAssetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Scene is outside project Assets folder: {absolutePath}");
            }

            return "Assets" + normalizedAbsolute.Substring(normalizedAssetsRoot.Length);
        }
    }
}
