using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Validates canonical task datasets before model training.
    /// </summary>
    public sealed class TaskDatasetQualityGate
    {
        public struct Requirements
        {
            public int minEvents;
            public int minTaskRuns;
            public float minLabelCoverageRatio;
            public float minSummaryCoverageRatio;
            public float minAverageLabelConfidence;
            public int minAdaptiveEvents;
            public bool requireMonotonicSequence;
        }

        public struct DatasetQualityReport
        {
            public bool readyForTraining;
            public string reasonCode;
            public int totalEvents;
            public int uniqueTaskRuns;
            public int taskOutcomeSummaries;
            public int taskLabels;
            public int adaptiveEvents;
            public int missingRequiredFields;
            public int duplicateEventIds;
            public int nonMonotonicSequenceCount;
            public int invalidSourceOfTruthCount;
            public int invalidLabelConfidenceCount;
            public int unmatchedLabelRuns;
            public float labelCoverageRatio;
            public float summaryCoverageRatio;
            public float averageLabelConfidence;
            public float minimumLabelConfidence;
            public float maximumLabelConfidence;
            public float qualityScore;
        }

        public static Requirements CreateDefaultRequirements()
        {
            return new Requirements
            {
                minEvents = 9,
                minTaskRuns = 3,
                minLabelCoverageRatio = 0.999f,
                minSummaryCoverageRatio = 0.999f,
                minAverageLabelConfidence = 0.55f,
                minAdaptiveEvents = 3,
                requireMonotonicSequence = true,
            };
        }

        public DatasetQualityReport Evaluate(
            IReadOnlyList<IReadOnlyDictionary<string, object>> records,
            Requirements requirements)
        {
            if (records == null || records.Count == 0)
            {
                return BuildReport(
                    reasonCode: "DATASET_EMPTY",
                    readyForTraining: false,
                    totalEvents: 0,
                    uniqueTaskRuns: 0,
                    taskOutcomeSummaries: 0,
                    taskLabels: 0,
                    adaptiveEvents: 0,
                    missingRequiredFields: 1,
                    duplicateEventIds: 0,
                    nonMonotonicSequenceCount: 0,
                    invalidSourceOfTruthCount: 0,
                    invalidLabelConfidenceCount: 0,
                    unmatchedLabelRuns: 0,
                    labelCoverageRatio: 0f,
                    summaryCoverageRatio: 0f,
                    averageLabelConfidence: 0f,
                    minimumLabelConfidence: 0f,
                    maximumLabelConfidence: 0f);
            }

            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            var uniqueTaskRuns = new HashSet<string>(StringComparer.Ordinal);
            var summaryTaskRuns = new HashSet<string>(StringComparer.Ordinal);
            var labelTaskRuns = new HashSet<string>(StringComparer.Ordinal);

            var missingRequiredFields = 0;
            var duplicateEventIds = 0;
            var nonMonotonicSequenceCount = 0;
            var invalidSourceOfTruthCount = 0;
            var invalidLabelConfidenceCount = 0;
            var taskOutcomeSummaries = 0;
            var taskLabels = 0;
            var adaptiveEvents = 0;
            var labelConfidenceSum = 0f;
            var labelConfidenceCount = 0;
            var minimumLabelConfidence = 1f;
            var maximumLabelConfidence = 0f;
            var previousSequence = 0L;

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null)
                {
                    missingRequiredFields++;
                    continue;
                }

                var eventId = ReadString(record, "eventId");
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    missingRequiredFields++;
                }
                else if (!eventIds.Add(eventId))
                {
                    duplicateEventIds++;
                }

                var eventType = ReadString(record, "eventType");
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    missingRequiredFields++;
                }

                var ownerKey = ReadString(record, "ownerKey");
                if (string.IsNullOrWhiteSpace(ownerKey))
                {
                    missingRequiredFields++;
                }

                var sessionKey = ReadString(record, "sessionKey");
                if (string.IsNullOrWhiteSpace(sessionKey))
                {
                    missingRequiredFields++;
                }

                var sourceOfTruth = ReadString(record, "sourceOfTruth");
                if (string.IsNullOrWhiteSpace(sourceOfTruth))
                {
                    missingRequiredFields++;
                }
                else if (!string.Equals(sourceOfTruth, "MOBILE_CONTROLLER", StringComparison.Ordinal))
                {
                    invalidSourceOfTruthCount++;
                }

                var sequence = ReadLong(record, "sequenceNumber");
                if (sequence <= 0)
                {
                    missingRequiredFields++;
                }
                else if (requirements.requireMonotonicSequence && previousSequence > 0 && sequence <= previousSequence)
                {
                    nonMonotonicSequenceCount++;
                }

                if (sequence > 0)
                {
                    previousSequence = sequence;
                }

                var topLevelTaskRunId = ReadString(record, "taskRunId");
                var detailsTaskRunId = ReadDetailsString(record, "taskRunId");
                var effectiveTaskRunId = !string.IsNullOrWhiteSpace(detailsTaskRunId)
                    ? detailsTaskRunId
                    : topLevelTaskRunId;

                if (string.IsNullOrWhiteSpace(topLevelTaskRunId))
                {
                    missingRequiredFields++;
                }

                if (!string.IsNullOrWhiteSpace(effectiveTaskRunId))
                {
                    uniqueTaskRuns.Add(effectiveTaskRunId);
                }

                if (string.Equals(eventType, "TASK_OUTCOME_SUMMARY", StringComparison.Ordinal))
                {
                    taskOutcomeSummaries++;
                    if (!string.IsNullOrWhiteSpace(effectiveTaskRunId))
                    {
                        summaryTaskRuns.Add(effectiveTaskRunId);
                    }
                }
                else if (string.Equals(eventType, "TASK_LABEL_GENERATED", StringComparison.Ordinal))
                {
                    taskLabels++;
                    if (!string.IsNullOrWhiteSpace(effectiveTaskRunId))
                    {
                        labelTaskRuns.Add(effectiveTaskRunId);
                    }

                    if (!TryReadDetailsFloat(record, "confidence", out var confidence))
                    {
                        invalidLabelConfidenceCount++;
                    }
                    else
                    {
                        confidence = Mathf.Clamp01(confidence);
                        labelConfidenceSum += confidence;
                        labelConfidenceCount++;
                        minimumLabelConfidence = Mathf.Min(minimumLabelConfidence, confidence);
                        maximumLabelConfidence = Mathf.Max(maximumLabelConfidence, confidence);
                    }
                }
                else if (string.Equals(eventType, "ADAPTIVE_DIFFICULTY_ADJUSTED", StringComparison.Ordinal))
                {
                    adaptiveEvents++;
                }
            }

            var unmatchedLabelRuns = CountSetDifference(labelTaskRuns, summaryTaskRuns);
            var labelCoverageRatio = uniqueTaskRuns.Count <= 0
                ? 0f
                : Mathf.Clamp01((float)labelTaskRuns.Count / uniqueTaskRuns.Count);
            var summaryCoverageRatio = uniqueTaskRuns.Count <= 0
                ? 0f
                : Mathf.Clamp01((float)summaryTaskRuns.Count / uniqueTaskRuns.Count);
            var averageLabelConfidence = labelConfidenceCount <= 0
                ? 0f
                : Mathf.Clamp01(labelConfidenceSum / labelConfidenceCount);

            if (labelConfidenceCount <= 0)
            {
                minimumLabelConfidence = 0f;
                maximumLabelConfidence = 0f;
            }

            var reasonCode = ResolveReasonCode(
                records.Count,
                uniqueTaskRuns.Count,
                taskOutcomeSummaries,
                taskLabels,
                adaptiveEvents,
                missingRequiredFields,
                duplicateEventIds,
                nonMonotonicSequenceCount,
                invalidSourceOfTruthCount,
                invalidLabelConfidenceCount,
                unmatchedLabelRuns,
                labelCoverageRatio,
                summaryCoverageRatio,
                averageLabelConfidence,
                requirements);

            var readyForTraining = string.Equals(reasonCode, "DATASET_READY_FOR_TRAINING", StringComparison.Ordinal);

            return BuildReport(
                reasonCode: reasonCode,
                readyForTraining: readyForTraining,
                totalEvents: records.Count,
                uniqueTaskRuns: uniqueTaskRuns.Count,
                taskOutcomeSummaries: taskOutcomeSummaries,
                taskLabels: taskLabels,
                adaptiveEvents: adaptiveEvents,
                missingRequiredFields: missingRequiredFields,
                duplicateEventIds: duplicateEventIds,
                nonMonotonicSequenceCount: nonMonotonicSequenceCount,
                invalidSourceOfTruthCount: invalidSourceOfTruthCount,
                invalidLabelConfidenceCount: invalidLabelConfidenceCount,
                unmatchedLabelRuns: unmatchedLabelRuns,
                labelCoverageRatio: labelCoverageRatio,
                summaryCoverageRatio: summaryCoverageRatio,
                averageLabelConfidence: averageLabelConfidence,
                minimumLabelConfidence: minimumLabelConfidence,
                maximumLabelConfidence: maximumLabelConfidence);
        }

        private static DatasetQualityReport BuildReport(
            string reasonCode,
            bool readyForTraining,
            int totalEvents,
            int uniqueTaskRuns,
            int taskOutcomeSummaries,
            int taskLabels,
            int adaptiveEvents,
            int missingRequiredFields,
            int duplicateEventIds,
            int nonMonotonicSequenceCount,
            int invalidSourceOfTruthCount,
            int invalidLabelConfidenceCount,
            int unmatchedLabelRuns,
            float labelCoverageRatio,
            float summaryCoverageRatio,
            float averageLabelConfidence,
            float minimumLabelConfidence,
            float maximumLabelConfidence)
        {
            var score = ComputeQualityScore(
                missingRequiredFields,
                duplicateEventIds,
                nonMonotonicSequenceCount,
                invalidSourceOfTruthCount,
                invalidLabelConfidenceCount,
                unmatchedLabelRuns,
                labelCoverageRatio,
                summaryCoverageRatio,
                averageLabelConfidence);

            return new DatasetQualityReport
            {
                readyForTraining = readyForTraining,
                reasonCode = NormalizeOrFallback(reasonCode, "DATASET_NOT_READY"),
                totalEvents = Mathf.Max(0, totalEvents),
                uniqueTaskRuns = Mathf.Max(0, uniqueTaskRuns),
                taskOutcomeSummaries = Mathf.Max(0, taskOutcomeSummaries),
                taskLabels = Mathf.Max(0, taskLabels),
                adaptiveEvents = Mathf.Max(0, adaptiveEvents),
                missingRequiredFields = Mathf.Max(0, missingRequiredFields),
                duplicateEventIds = Mathf.Max(0, duplicateEventIds),
                nonMonotonicSequenceCount = Mathf.Max(0, nonMonotonicSequenceCount),
                invalidSourceOfTruthCount = Mathf.Max(0, invalidSourceOfTruthCount),
                invalidLabelConfidenceCount = Mathf.Max(0, invalidLabelConfidenceCount),
                unmatchedLabelRuns = Mathf.Max(0, unmatchedLabelRuns),
                labelCoverageRatio = Mathf.Clamp01(labelCoverageRatio),
                summaryCoverageRatio = Mathf.Clamp01(summaryCoverageRatio),
                averageLabelConfidence = Mathf.Clamp01(averageLabelConfidence),
                minimumLabelConfidence = Mathf.Clamp01(minimumLabelConfidence),
                maximumLabelConfidence = Mathf.Clamp01(maximumLabelConfidence),
                qualityScore = score,
            };
        }

        private static float ComputeQualityScore(
            int missingRequiredFields,
            int duplicateEventIds,
            int nonMonotonicSequenceCount,
            int invalidSourceOfTruthCount,
            int invalidLabelConfidenceCount,
            int unmatchedLabelRuns,
            float labelCoverageRatio,
            float summaryCoverageRatio,
            float averageLabelConfidence)
        {
            var score = 1f;
            score -= Mathf.Min(0.45f, Mathf.Max(0, missingRequiredFields) * 0.03f);
            score -= Mathf.Min(0.25f, Mathf.Max(0, duplicateEventIds) * 0.08f);
            score -= Mathf.Min(0.2f, Mathf.Max(0, nonMonotonicSequenceCount) * 0.07f);
            score -= Mathf.Min(0.2f, Mathf.Max(0, invalidSourceOfTruthCount) * 0.1f);
            score -= Mathf.Min(0.2f, Mathf.Max(0, invalidLabelConfidenceCount) * 0.08f);
            score -= Mathf.Min(0.2f, Mathf.Max(0, unmatchedLabelRuns) * 0.05f);
            score -= Mathf.Clamp01(1f - Mathf.Clamp01(labelCoverageRatio)) * 0.35f;
            score -= Mathf.Clamp01(1f - Mathf.Clamp01(summaryCoverageRatio)) * 0.25f;
            score -= Mathf.Clamp01(0.6f - Mathf.Clamp01(averageLabelConfidence)) * 0.3f;
            return Mathf.Clamp01(score);
        }

        private static string ResolveReasonCode(
            int totalEvents,
            int uniqueTaskRuns,
            int taskOutcomeSummaries,
            int taskLabels,
            int adaptiveEvents,
            int missingRequiredFields,
            int duplicateEventIds,
            int nonMonotonicSequenceCount,
            int invalidSourceOfTruthCount,
            int invalidLabelConfidenceCount,
            int unmatchedLabelRuns,
            float labelCoverageRatio,
            float summaryCoverageRatio,
            float averageLabelConfidence,
            Requirements requirements)
        {
            if (totalEvents < Mathf.Max(1, requirements.minEvents))
            {
                return "INSUFFICIENT_EVENT_COUNT";
            }

            if (uniqueTaskRuns < Mathf.Max(1, requirements.minTaskRuns))
            {
                return "INSUFFICIENT_TASK_RUNS";
            }

            if (taskOutcomeSummaries < Mathf.Max(1, requirements.minTaskRuns))
            {
                return "MISSING_TASK_SUMMARIES";
            }

            if (taskLabels <= 0 || labelCoverageRatio < Mathf.Clamp01(requirements.minLabelCoverageRatio))
            {
                return "INSUFFICIENT_LABEL_COVERAGE";
            }

            if (summaryCoverageRatio < Mathf.Clamp01(requirements.minSummaryCoverageRatio))
            {
                return "INSUFFICIENT_SUMMARY_COVERAGE";
            }

            if (adaptiveEvents < Mathf.Max(1, requirements.minAdaptiveEvents))
            {
                return "INSUFFICIENT_ADAPTIVE_EVENTS";
            }

            if (missingRequiredFields > 0)
            {
                return "MISSING_REQUIRED_FIELDS";
            }

            if (duplicateEventIds > 0)
            {
                return "DUPLICATE_EVENT_IDS";
            }

            if (invalidSourceOfTruthCount > 0)
            {
                return "INVALID_SOURCE_OF_TRUTH";
            }

            if (invalidLabelConfidenceCount > 0)
            {
                return "INVALID_LABEL_CONFIDENCE";
            }

            if (requirements.requireMonotonicSequence && nonMonotonicSequenceCount > 0)
            {
                return "NON_MONOTONIC_SEQUENCE";
            }

            if (unmatchedLabelRuns > 0)
            {
                return "UNMATCHED_LABEL_RUN";
            }

            if (averageLabelConfidence < Mathf.Clamp01(requirements.minAverageLabelConfidence))
            {
                return "LOW_LABEL_CONFIDENCE";
            }

            return "DATASET_READY_FOR_TRAINING";
        }

        private static int CountSetDifference(
            IReadOnlyCollection<string> left,
            IReadOnlyCollection<string> right)
        {
            if (left == null || left.Count <= 0)
            {
                return 0;
            }

            var rightSet = right != null
                ? new HashSet<string>(right, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var differenceCount = 0;

            foreach (var value in left)
            {
                if (!rightSet.Contains(value))
                {
                    differenceCount++;
                }
            }

            return differenceCount;
        }

        private static string ReadString(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static long ReadLong(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var value) || value == null)
            {
                return 0L;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;
                case long longValue:
                    return longValue;
                case float floatValue:
                    return (long)floatValue;
                case double doubleValue:
                    return (long)doubleValue;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0L;
            }

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0L;
        }

        private static string ReadDetailsString(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!record.TryGetValue("details", out var detailsValue) || detailsValue == null)
            {
                return string.Empty;
            }

            if (!TryGetDetailsMap(detailsValue, out var detailsMap) ||
                !detailsMap.TryGetValue(key, out var value) ||
                value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static bool TryReadDetailsFloat(
            IReadOnlyDictionary<string, object> record,
            string key,
            out float value)
        {
            value = 0f;
            if (record == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!record.TryGetValue("details", out var detailsValue) || detailsValue == null)
            {
                return false;
            }

            if (!TryGetDetailsMap(detailsValue, out var detailsMap) ||
                !detailsMap.TryGetValue(key, out var rawValue) ||
                rawValue == null)
            {
                return false;
            }

            switch (rawValue)
            {
                case float floatValue:
                    value = floatValue;
                    return true;
                case double doubleValue:
                    value = (float)doubleValue;
                    return true;
                case decimal decimalValue:
                    value = (float)decimalValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
            }

            var text = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetDetailsMap(object detailsValue, out IReadOnlyDictionary<string, object> detailsMap)
        {
            detailsMap = null;

            if (detailsValue is IReadOnlyDictionary<string, object> readOnlyMap)
            {
                detailsMap = readOnlyMap;
                return true;
            }

            if (detailsValue is Dictionary<string, object> mutableMap)
            {
                detailsMap = mutableMap;
                return true;
            }

            return false;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
