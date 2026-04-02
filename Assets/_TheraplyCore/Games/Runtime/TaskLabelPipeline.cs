using System;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Produces ML-ready labels from canonical task outcomes and adaptive decisions.
    /// </summary>
    public sealed class TaskLabelPipeline
    {
        public const string LabelSchema = "THERAPLY_TASK_LABEL_SCHEMA";
        public const string LabelVersion = "2026-02-22";

        public struct TaskLabel
        {
            public string labelId;
            public string labelSchema;
            public string labelVersion;
            public string labelType;
            public string taskRunId;
            public string gameId;
            public string performanceBand;
            public string paceBand;
            public string fatigueBand;
            public string adaptationRecommendation;
            public string reasonCode;
            public float confidence;
            public float completionRatio;
            public float averageReactionSec;
            public float omittedRatio;
            public float lateRatio;
            public float difficultyScore;
            public int recommendedDifficultyLevel;
        }

        public TaskLabel CreateTaskLabel(
            TaskOutcomeAggregator.TaskOutcomeSummary summary,
            AdaptiveDifficultyController.DifficultyDecision decision)
        {
            var completionRatio = Mathf.Clamp01(summary.completionRatio);
            var averageReactionSec = Mathf.Max(0f, summary.averageReactionSec);
            var omittedRatio = ResolveRatio(summary.omittedCount, summary.cuesPresented);
            var lateRatio = ResolveRatio(summary.lateCount, summary.cuesPresented);
            var sampleCoverage = Mathf.Clamp01((float)Mathf.Max(0, summary.cuesPresented) / 8f);
            var noisePenalty = Mathf.Clamp01((omittedRatio + lateRatio) * 0.65f);
            var confidence = Mathf.Clamp01(0.55f + sampleCoverage * 0.35f - noisePenalty * 0.25f);
            var difficultyScore = Mathf.Clamp01(decision.nextDifficulty);

            return new TaskLabel
            {
                labelId = Guid.NewGuid().ToString(),
                labelSchema = LabelSchema,
                labelVersion = LabelVersion,
                labelType = "TASK_RUN_SUPERVISED_LABEL",
                taskRunId = NormalizeOrFallback(summary.taskRunId, string.Empty),
                gameId = NormalizeOrFallback(summary.gameId, string.Empty),
                performanceBand = ResolvePerformanceBand(completionRatio),
                paceBand = ResolvePaceBand(averageReactionSec),
                fatigueBand = ResolveFatigueBand(omittedRatio, lateRatio),
                adaptationRecommendation = ResolveAdaptationRecommendation(decision.reasonCode),
                reasonCode = "TASK_LABEL_GENERATED",
                confidence = confidence,
                completionRatio = completionRatio,
                averageReactionSec = averageReactionSec,
                omittedRatio = omittedRatio,
                lateRatio = lateRatio,
                difficultyScore = difficultyScore,
                recommendedDifficultyLevel = ResolveDifficultyLevel(difficultyScore),
            };
        }

        private static string ResolvePerformanceBand(float completionRatio)
        {
            if (completionRatio >= 0.84f)
            {
                return "PERFORMANCE_HIGH";
            }

            if (completionRatio >= 0.58f)
            {
                return "PERFORMANCE_MEDIUM";
            }

            return "PERFORMANCE_LOW";
        }

        private static string ResolvePaceBand(float averageReactionSec)
        {
            if (averageReactionSec <= 0.45f)
            {
                return "PACE_FAST";
            }

            if (averageReactionSec <= 0.95f)
            {
                return "PACE_STABLE";
            }

            return "PACE_SLOW";
        }

        private static string ResolveFatigueBand(float omittedRatio, float lateRatio)
        {
            if (omittedRatio >= 0.33f || lateRatio >= 0.33f)
            {
                return "FATIGUE_HIGH";
            }

            if (omittedRatio >= 0.15f || lateRatio >= 0.2f)
            {
                return "FATIGUE_MEDIUM";
            }

            return "FATIGUE_LOW";
        }

        private static string ResolveAdaptationRecommendation(string reasonCode)
        {
            var normalizedReason = NormalizeOrFallback(reasonCode, string.Empty).ToUpperInvariant();
            if (normalizedReason.Contains("INCREASE"))
            {
                return "INCREASE_DIFFICULTY";
            }

            if (normalizedReason.Contains("DECREASE"))
            {
                return "DECREASE_DIFFICULTY";
            }

            return "KEEP_DIFFICULTY";
        }

        private static int ResolveDifficultyLevel(float difficultyScore)
        {
            return Mathf.Clamp(Mathf.RoundToInt(1f + Mathf.Clamp01(difficultyScore) * 4f), 1, 5);
        }

        private static float ResolveRatio(int numerator, int denominator)
        {
            var safeNumerator = Mathf.Max(0, numerator);
            var safeDenominator = Mathf.Max(1, denominator);
            return Mathf.Clamp01((float)safeNumerator / safeDenominator);
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
