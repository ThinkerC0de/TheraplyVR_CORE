using System;
using System.IO;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class NarratorLocalizationRuntimeValidation
    {
        public static void RunNarratorLocalizationRuntimeValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[NarratorLocalizationRuntimeValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[NarratorLocalizationRuntimeValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject runtimeHost = null;
            try
            {
                runtimeHost = new GameObject("NarratorLocalizationRuntimeValidationHost");
                runtimeHost.SetActive(false);

                var localizationRuntime = runtimeHost.AddComponent<LocalizationRuntime>();
                var narratorRuntime = runtimeHost.AddComponent<NarratorRuntime>();

                SetNonPublicField(localizationRuntime, "_emitLocalizationTelemetry", false);
                SetNonPublicField(narratorRuntime, "_emitNarratorTelemetry", false);
                SetNonPublicField(narratorRuntime, "_logNarrator", false);

                runtimeHost.SetActive(true);

                ValidateLocalizationFallbackAndMissing(localizationRuntime);
                ValidateNarratorQueueAndInterrupt(narratorRuntime);

                return "localizationFallback=OK; localizationMissing=OK; narratorQueueInterrupt=OK";
            }
            finally
            {
                if (runtimeHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeHost);
                }
            }
        }

        private static void ValidateLocalizationFallbackAndMissing(LocalizationRuntime localizationRuntime)
        {
            AssertTrue(localizationRuntime != null, "Localization runtime is required.");

            localizationRuntime.ApplyPolicy(
                new LocalizationPolicy
                {
                    defaultLocale = "en-US",
                    fallbackToLanguageCode = true,
                });
            localizationRuntime.SetValue("pl", "narrator.welcome.text", "Czesc");
            localizationRuntime.SetValue("en-US", "narrator.welcome.text", "Hello");

            var fallbackUsedCount = 0;
            var missingKeyCount = 0;
            localizationRuntime.LocalizationFallbackUsed += (_, _, _) => fallbackUsedCount++;
            localizationRuntime.LocalizationKeyMissing += (_, _) => missingKeyCount++;

            AssertTrue(
                localizationRuntime.TrySetLocale("pl-PL", out var setLocaleReason),
                "Set locale should pass. reason=" + setLocaleReason);
            AssertTrue(
                localizationRuntime.TryResolve(
                    "narrator.welcome.text",
                    out var resolvedValue,
                    out var resolvedLocale,
                    out var resolveReason),
                "Localized key should resolve via language fallback. reason=" + resolveReason);
            AssertEqual("Czesc", resolvedValue, "Unexpected localized value.");
            AssertEqual("pl", resolvedLocale, "Unexpected resolved locale.");
            AssertTrue(fallbackUsedCount == 1, "Expected one localization fallback event.");

            AssertFalse(
                localizationRuntime.TryResolve(
                    "narrator.missing.text",
                    out _,
                    out _,
                    out var missingReason),
                "Missing localization key should fail.");
            AssertEqual("LOCALIZATION_KEY_NOT_FOUND", missingReason, "Unexpected missing-key reason code.");
            AssertTrue(missingKeyCount == 1, "Expected one localization key missing event.");
        }

        private static void ValidateNarratorQueueAndInterrupt(NarratorRuntime narratorRuntime)
        {
            AssertTrue(narratorRuntime != null, "Narrator runtime is required.");

            var startedCount = 0;
            var interruptedCount = 0;
            var completedCount = 0;
            narratorRuntime.LineStarted += _ => startedCount++;
            narratorRuntime.LineInterrupted += (_, _) => interruptedCount++;
            narratorRuntime.LineCompleted += _ => completedCount++;

            AssertTrue(
                narratorRuntime.TrySpeak(
                    new NarratorLineRequest
                    {
                        lineKey = "narrator.line_one",
                        fallbackText = "line one",
                        priority = 1,
                        simulatedDurationSec = 5f,
                    },
                    out var lineOneReason),
                "First narrator line should start. reason=" + lineOneReason);
            AssertEqual("narrator.line_one", narratorRuntime.ActiveLineKey, "Unexpected active line after first start.");

            AssertTrue(
                narratorRuntime.TrySpeak(
                    new NarratorLineRequest
                    {
                        lineKey = "narrator.line_two",
                        fallbackText = "line two",
                        priority = 0,
                        simulatedDurationSec = 5f,
                    },
                    out var lineTwoReason),
                "Second narrator line should be queued. reason=" + lineTwoReason);
            AssertTrue(narratorRuntime.PendingCount == 1, "Expected one queued narrator line.");

            AssertTrue(
                narratorRuntime.TrySpeak(
                    new NarratorLineRequest
                    {
                        lineKey = "narrator.line_three",
                        fallbackText = "line three",
                        priority = 5,
                        interruptIfBusy = true,
                        simulatedDurationSec = 1f,
                    },
                    out var lineThreeReason),
                "Third narrator line should interrupt active line. reason=" + lineThreeReason);
            AssertEqual("narrator.line_three", narratorRuntime.ActiveLineKey, "Unexpected active line after interrupt.");
            AssertTrue(interruptedCount == 1, "Expected one narrator interrupt event.");
            AssertTrue(narratorRuntime.PendingCount == 1, "Queue should still hold one pending line after interrupt.");

            narratorRuntime.TickRuntime(Time.realtimeSinceStartup + 2f);
            AssertEqual("narrator.line_two", narratorRuntime.ActiveLineKey, "Queued line should become active after completion.");

            narratorRuntime.TickRuntime(Time.realtimeSinceStartup + 10f);
            AssertTrue(!narratorRuntime.IsSpeaking, "Narrator should be idle after completing queued lines.");
            AssertTrue(startedCount == 3, "Expected three narrator line started events.");
            AssertTrue(completedCount == 2, "Expected two narrator completed events (interrupted line excluded).");
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "narrator_localization_runtime_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);
            File.WriteAllLines(
                outputPath,
                new[]
                {
                    $"status={status}",
                    $"timestampUtc={DateTime.UtcNow:O}",
                    $"details={details ?? string.Empty}",
                });

            Debug.Log("[NarratorLocalizationRuntimeValidation] Result file: " + outputPath);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual(string expected, string actual, string message)
        {
            var safeExpected = expected ?? string.Empty;
            var safeActual = actual ?? string.Empty;
            if (!string.Equals(safeExpected, safeActual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{message} expected={safeExpected} actual={safeActual}");
            }
        }
    }
}
