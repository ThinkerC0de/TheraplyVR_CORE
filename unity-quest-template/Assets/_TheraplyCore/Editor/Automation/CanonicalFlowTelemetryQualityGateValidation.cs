using System;
using System.Collections.Generic;
using System.IO;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class CanonicalFlowTelemetryQualityGateValidation
    {
        public static void RunCanonicalFlowTelemetryQualityGateValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[CanonicalFlowTelemetryQualityGateValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[CanonicalFlowTelemetryQualityGateValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var gate = new CanonicalFlowTelemetryQualityGate();
            var requirements = CanonicalFlowTelemetryQualityGate.CreateDefaultRequirements();

            var validRecords = CreateValidFlowRecords();
            var validReport = gate.Evaluate(validRecords, requirements);
            AssertTrue(validReport.readyForExport, "Expected valid flow telemetry report to pass.");
            AssertEqual("FLOW_TELEMETRY_READY", validReport.reasonCode, "Unexpected reason for valid flow telemetry.");

            var unresolvedDecisionRecords = new List<IReadOnlyDictionary<string, object>>(validRecords);
            unresolvedDecisionRecords.RemoveAt(2); // remove ACTION_EVALUATED
            var unresolvedDecisionReport = gate.Evaluate(unresolvedDecisionRecords, requirements);
            AssertFalse(unresolvedDecisionReport.readyForExport, "Expected unresolved decision report to fail.");
            AssertEqual(
                "FLOW_ACTION_DECISION_COVERAGE_FAILED",
                unresolvedDecisionReport.reasonCode,
                "Unexpected reason for unresolved decision report.");

            var sequenceGapRecords = CreateValidFlowRecords();
            sequenceGapRecords[2]["sequenceNumber"] = 10L; // create sequence gap
            var sequenceGapReport = gate.Evaluate(sequenceGapRecords, requirements);
            AssertFalse(sequenceGapReport.readyForExport, "Expected sequence gap report to fail.");
            AssertEqual(
                "FLOW_SEQUENCE_GAPS_DETECTED",
                sequenceGapReport.reasonCode,
                "Unexpected reason for sequence gap report.");

            var missingTerminalRecords = new List<IReadOnlyDictionary<string, object>>(CreateValidFlowRecords());
            missingTerminalRecords.RemoveAt(missingTerminalRecords.Count - 1); // remove SESSION_TERMINAL
            var missingTerminalReport = gate.Evaluate(missingTerminalRecords, requirements);
            AssertFalse(missingTerminalReport.readyForExport, "Expected missing terminal report to fail.");
            AssertEqual(
                "FLOW_SESSION_TERMINAL_MISSING",
                missingTerminalReport.reasonCode,
                "Unexpected reason for missing terminal report.");

            return "valid=PASS; unresolvedDecision=PASS; sequenceGap=PASS; missingTerminal=PASS";
        }

        private static List<Dictionary<string, object>> CreateValidFlowRecords()
        {
            var sessionId = "validation_session";
            var taskRunId = "validation_task_run";
            var attemptId = "validation_attempt";
            var actionAttemptId = "validation_action_attempt";

            var records = new List<Dictionary<string, object>>
            {
                CreateRecord("FLOW_STARTED", 1, sessionId, taskRunId, attemptId, string.Empty),
                CreateRecord("ACTION_RECEIVED", 2, sessionId, taskRunId, attemptId, actionAttemptId),
                CreateRecord("ACTION_EVALUATED", 3, sessionId, taskRunId, attemptId, actionAttemptId),
                CreateRecord("FLOW_COMPLETED", 4, sessionId, taskRunId, attemptId, string.Empty),
                CreateRecord("SESSION_TERMINAL", 5, sessionId, taskRunId, attemptId, string.Empty),
            };

            return records;
        }

        private static Dictionary<string, object> CreateRecord(
            string eventType,
            long sequenceNumber,
            string sessionId,
            string taskRunId,
            string attemptId,
            string actionAttemptId)
        {
            var eventId = Guid.NewGuid().ToString("N");
            return new Dictionary<string, object>
            {
                { "eventId", eventId },
                { "sessionId", sessionId },
                { "taskRunId", taskRunId },
                { "attemptId", attemptId },
                { "sequenceNumber", sequenceNumber },
                { "gameId", "validation_game" },
                { "eventType", eventType },
                { "occurredAtUtc", DateTime.UtcNow.ToString("O") },
                { "sourceComponent", "CanonicalFlowTelemetryQualityGateValidation" },
                { "payloadVersion", 1 },
                { "actionAttemptId", actionAttemptId ?? string.Empty },
                {
                    "details",
                    new Dictionary<string, object>
                    {
                        { "actionAttemptId", actionAttemptId ?? string.Empty },
                    }
                },
            };
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "canonical_flow_telemetry_quality_gate_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[CanonicalFlowTelemetryQualityGateValidation] Result file: " + outputPath);
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
