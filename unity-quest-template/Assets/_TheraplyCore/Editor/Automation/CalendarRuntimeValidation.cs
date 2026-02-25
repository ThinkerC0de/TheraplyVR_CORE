using System;
using System.Collections.Generic;
using System.IO;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class CalendarRuntimeValidation
    {
        private sealed class FixedTimeSource : ICalendarTimeSource
        {
            public DateTime UtcNow { get; set; }
        }

        public static void RunCalendarRuntimeValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[CalendarRuntimeValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[CalendarRuntimeValidation] FAIL: " + exception);
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
                runtimeHost = new GameObject("CalendarRuntimeValidationHost");
                runtimeHost.SetActive(false);

                var calendarRuntime = runtimeHost.AddComponent<CalendarRuntime>();
                SetNonPublicField(calendarRuntime, "_emitCalendarTelemetry", false);
                SetNonPublicField(calendarRuntime, "_logCalendar", false);
                SetNonPublicField(calendarRuntime, "_evaluationIntervalSec", 0f);

                runtimeHost.SetActive(true);

                ValidateYearlyAndCrossYearWindows(calendarRuntime);
                ValidateTimezoneWindow(calendarRuntime);
                ValidateEventPriorityConflict(calendarRuntime);
                ValidateProfileBirthday(calendarRuntime);

                return "yearlyCrossYear=OK; timezoneWindow=OK; conflictPolicy=OK; profileBirthday=OK";
            }
            finally
            {
                if (runtimeHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeHost);
                }
            }
        }

        private static void ValidateYearlyAndCrossYearWindows(CalendarRuntime calendarRuntime)
        {
            AssertTrue(calendarRuntime != null, "Calendar runtime is required.");

            var fixedTime = new FixedTimeSource
            {
                UtcNow = new DateTime(2026, 12, 25, 12, 0, 0, DateTimeKind.Utc),
            };
            calendarRuntime.SetTimeSource(fixedTime);

            AssertTrue(
                calendarRuntime.TryApplyPolicy(
                    new CalendarPolicy
                    {
                        timezoneId = "UTC",
                    },
                    profileDatesByKey: null,
                    out var policyReason),
                "Calendar policy should apply. reason=" + policyReason);

            AssertTrue(
                calendarRuntime.TryEvaluateDateWindow(
                    "2020-12-24T00:00:00Z",
                    "2020-12-31T23:59:59Z",
                    yearlyRecurring: true,
                    timezoneId: "UTC",
                    out var yearlyMatch,
                    out var yearlyReason),
                "Yearly window should evaluate. reason=" + yearlyReason);
            AssertTrue(yearlyMatch, "Expected yearly recurring window to match.");

            fixedTime.UtcNow = new DateTime(2027, 1, 5, 10, 0, 0, DateTimeKind.Utc);
            AssertTrue(
                calendarRuntime.TryEvaluateDateWindow(
                    "2020-12-20T00:00:00Z",
                    "2021-01-10T23:59:59Z",
                    yearlyRecurring: true,
                    timezoneId: "UTC",
                    out var crossYearMatch,
                    out var crossYearReason),
                "Cross-year recurring window should evaluate. reason=" + crossYearReason);
            AssertTrue(crossYearMatch, "Expected cross-year recurring window to match in January.");

            fixedTime.UtcNow = new DateTime(2027, 2, 10, 10, 0, 0, DateTimeKind.Utc);
            AssertTrue(
                calendarRuntime.TryEvaluateDateWindow(
                    "2020-12-20T00:00:00Z",
                    "2021-01-10T23:59:59Z",
                    yearlyRecurring: true,
                    timezoneId: "UTC",
                    out var outOfRangeMatch,
                    out var outOfRangeReason),
                "Cross-year recurring window should evaluate outside range. reason=" + outOfRangeReason);
            AssertFalse(outOfRangeMatch, "Expected recurring window to be outside of range.");
        }

        private static void ValidateTimezoneWindow(CalendarRuntime calendarRuntime)
        {
            AssertTrue(calendarRuntime != null, "Calendar runtime is required.");

            var fixedTime = new FixedTimeSource
            {
                UtcNow = new DateTime(2026, 12, 24, 7, 30, 0, DateTimeKind.Utc),
            };
            calendarRuntime.SetTimeSource(fixedTime);

            AssertTrue(
                calendarRuntime.TryEvaluateDateWindow(
                    "2026-12-24T00:00:00-08:00",
                    "2026-12-24T23:59:59-08:00",
                    yearlyRecurring: false,
                    timezoneId: "Pacific Standard Time",
                    out var beforeWindowMatch,
                    out var beforeWindowReason),
                "Timezone window should evaluate before local start. reason=" + beforeWindowReason);
            AssertFalse(beforeWindowMatch, "Expected timezone window to be inactive before local midnight.");

            fixedTime.UtcNow = new DateTime(2026, 12, 24, 9, 10, 0, DateTimeKind.Utc);
            AssertTrue(
                calendarRuntime.TryEvaluateDateWindow(
                    "2026-12-24T00:00:00-08:00",
                    "2026-12-24T23:59:59-08:00",
                    yearlyRecurring: false,
                    timezoneId: "Pacific Standard Time",
                    out var withinWindowMatch,
                    out var withinWindowReason),
                "Timezone window should evaluate during local day. reason=" + withinWindowReason);
            AssertTrue(withinWindowMatch, "Expected timezone window to match after local midnight.");
        }

        private static void ValidateEventPriorityConflict(CalendarRuntime calendarRuntime)
        {
            AssertTrue(calendarRuntime != null, "Calendar runtime is required.");

            var fixedTime = new FixedTimeSource
            {
                UtcNow = new DateTime(2026, 12, 24, 12, 0, 0, DateTimeKind.Utc),
            };
            calendarRuntime.SetTimeSource(fixedTime);

            var profileDates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var highestPriorityPolicy = new CalendarPolicy
            {
                timezoneId = "UTC",
                conflictPolicy = CalendarConflictPolicies.HighestPriority,
                rules = new List<CalendarRuleDefinition>
                {
                    new CalendarRuleDefinition
                    {
                        ruleId = "window_rule",
                        ruleType = CalendarRuleTypes.DateWindow,
                        start = "2026-12-20T00:00:00Z",
                        end = "2026-12-30T23:59:59Z",
                    },
                },
                events = new List<CalendarEventDefinition>
                {
                    new CalendarEventDefinition
                    {
                        eventId = "event_low",
                        variantGroup = "season_variant",
                        priority = 10,
                        requireAllRules = true,
                        ruleIds = new List<string> { "window_rule" },
                    },
                    new CalendarEventDefinition
                    {
                        eventId = "event_high",
                        variantGroup = "season_variant",
                        priority = 20,
                        requireAllRules = true,
                        ruleIds = new List<string> { "window_rule" },
                    },
                },
            };

            AssertTrue(
                calendarRuntime.TryApplyPolicy(highestPriorityPolicy, profileDates, out var applyReason),
                "Highest-priority conflict policy should apply. reason=" + applyReason);
            calendarRuntime.TickRuntime();

            AssertTrue(
                calendarRuntime.TryEvaluateEventActive("event_high", out var highActive, out var highReason),
                "High-priority event should evaluate. reason=" + highReason);
            AssertTrue(highActive, "High-priority event should be active.");
            AssertTrue(
                calendarRuntime.TryEvaluateEventActive("event_low", out var lowActive, out var lowReason),
                "Low-priority event should evaluate. reason=" + lowReason);
            AssertFalse(lowActive, "Low-priority event should be suppressed by conflict policy.");
        }

        private static void ValidateProfileBirthday(CalendarRuntime calendarRuntime)
        {
            AssertTrue(calendarRuntime != null, "Calendar runtime is required.");

            var fixedTime = new FixedTimeSource
            {
                UtcNow = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc),
            };
            calendarRuntime.SetTimeSource(fixedTime);

            AssertTrue(
                calendarRuntime.TryApplyPolicy(
                    new CalendarPolicy
                    {
                        timezoneId = "UTC",
                        defaultProfileDateKey = "profile_birthday",
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "profile_birthday", "2010-02-10" },
                    },
                    out var applyReason),
                "Profile birthday policy should apply. reason=" + applyReason);

            AssertTrue(
                calendarRuntime.TryEvaluateProfileDate(
                    "profile_birthday",
                    daysBefore: 0,
                    daysAfter: 0,
                    timezoneId: "UTC",
                    out var exactMatch,
                    out var exactReason),
                "Profile birthday should evaluate. reason=" + exactReason);
            AssertTrue(exactMatch, "Expected exact birthday match.");

            fixedTime.UtcNow = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc);
            AssertTrue(
                calendarRuntime.TryEvaluateProfileDate(
                    "profile_birthday",
                    daysBefore: 1,
                    daysAfter: 1,
                    timezoneId: "UTC",
                    out var toleranceMatch,
                    out var toleranceReason),
                "Profile birthday with tolerance should evaluate. reason=" + toleranceReason);
            AssertFalse(toleranceMatch, "Expected birthday to be outside tolerance window.");
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
                "calendar_runtime_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);
            File.WriteAllLines(
                outputPath,
                new[]
                {
                    $"status={status}",
                    $"timestampUtc={DateTime.UtcNow:O}",
                    $"details={details ?? string.Empty}",
                });

            Debug.Log("[CalendarRuntimeValidation] Result file: " + outputPath);
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
    }
}
