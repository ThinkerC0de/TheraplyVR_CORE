using System;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Adjusts gameplay difficulty from canonical task outcomes without blocking frame updates.
    /// </summary>
    public sealed class AdaptiveDifficultyController
    {
        public struct DifficultyPolicy
        {
            public bool enabled;
            public float sensitivity;
            public float minTargetSpeed;
            public float maxTargetSpeed;
            public float minTargetScale;
            public float maxTargetScale;
            public float minCueTimeoutSec;
            public float maxCueTimeoutSec;
            public float promoteCompletionRatio;
            public float demoteCompletionRatio;
            public float promoteReactionSec;
            public float demoteOmittedRatio;
            public float demoteLateRatio;
        }

        public struct DifficultyDecision
        {
            public bool enabled;
            public bool changed;
            public string reasonCode;
            public float previousDifficulty;
            public float nextDifficulty;
            public float delta;
            public float sensitivity;
            public float completionRatio;
            public float averageReactionSec;
            public float omittedRatio;
            public float lateRatio;
            public float targetSpeed;
            public float targetScale;
            public float cueTimeoutSec;
        }

        private DifficultyPolicy _policy;
        private float _difficulty;
        private bool _configured;

        public DifficultyDecision CurrentDecision
        {
            get
            {
                EnsureConfigured();
                return BuildDecision(
                    changed: false,
                    reasonCode: _policy.enabled ? "KEEP_DIFFICULTY" : "ADAPTIVE_DISABLED",
                    previousDifficulty: _difficulty,
                    nextDifficulty: _difficulty,
                    completionRatio: 0f,
                    averageReactionSec: 0f,
                    omittedRatio: 0f,
                    lateRatio: 0f);
            }
        }

        public static DifficultyPolicy CreateDefaultPolicy()
        {
            return new DifficultyPolicy
            {
                enabled = true,
                sensitivity = 0.55f,
                minTargetSpeed = 0.35f,
                maxTargetSpeed = 1.6f,
                minTargetScale = 0.18f,
                maxTargetScale = 0.62f,
                minCueTimeoutSec = 0.75f,
                maxCueTimeoutSec = 3.2f,
                promoteCompletionRatio = 0.84f,
                demoteCompletionRatio = 0.56f,
                promoteReactionSec = 0.62f,
                demoteOmittedRatio = 0.26f,
                demoteLateRatio = 0.24f,
            };
        }

        public void Configure(DifficultyPolicy policy, float startingDifficulty)
        {
            _policy = NormalizePolicy(policy);
            _difficulty = Mathf.Clamp01(startingDifficulty);
            _configured = true;
        }

        public DifficultyDecision Evaluate(TaskOutcomeAggregator.TaskOutcomeSummary summary)
        {
            EnsureConfigured();
            if (!_policy.enabled)
            {
                return BuildDecision(
                    changed: false,
                    reasonCode: "ADAPTIVE_DISABLED",
                    previousDifficulty: _difficulty,
                    nextDifficulty: _difficulty,
                    completionRatio: summary.completionRatio,
                    averageReactionSec: summary.averageReactionSec,
                    omittedRatio: ResolveRatio(summary.omittedCount, summary.cuesPresented),
                    lateRatio: ResolveRatio(summary.lateCount, summary.cuesPresented));
            }

            var completionRatio = Mathf.Clamp01(summary.completionRatio);
            var averageReactionSec = Mathf.Max(0f, summary.averageReactionSec);
            var omittedRatio = ResolveRatio(summary.omittedCount, summary.cuesPresented);
            var lateRatio = ResolveRatio(summary.lateCount, summary.cuesPresented);

            var shouldPromote =
                completionRatio >= _policy.promoteCompletionRatio &&
                averageReactionSec <= _policy.promoteReactionSec &&
                omittedRatio <= (_policy.demoteOmittedRatio * 0.6f);
            var shouldDemote =
                completionRatio <= _policy.demoteCompletionRatio ||
                omittedRatio >= _policy.demoteOmittedRatio ||
                lateRatio >= _policy.demoteLateRatio;

            var previousDifficulty = _difficulty;
            var delta = 0f;
            var reasonCode = "KEEP_DIFFICULTY";

            if (shouldPromote && !shouldDemote)
            {
                delta = ResolveStep();
                reasonCode = "INCREASE_DIFFICULTY";
            }
            else if (shouldDemote && !shouldPromote)
            {
                delta = -ResolveStep();
                reasonCode = "DECREASE_DIFFICULTY";
            }
            else if (shouldPromote && shouldDemote)
            {
                reasonCode = "KEEP_DIFFICULTY_MIXED_SIGNALS";
            }

            _difficulty = Mathf.Clamp01(_difficulty + delta);
            var changed = Mathf.Abs(_difficulty - previousDifficulty) > 0.0001f;

            return BuildDecision(
                changed: changed,
                reasonCode: reasonCode,
                previousDifficulty: previousDifficulty,
                nextDifficulty: _difficulty,
                completionRatio: completionRatio,
                averageReactionSec: averageReactionSec,
                omittedRatio: omittedRatio,
                lateRatio: lateRatio);
        }

        private void EnsureConfigured()
        {
            if (_configured)
            {
                return;
            }

            Configure(CreateDefaultPolicy(), 0.5f);
        }

        private DifficultyDecision BuildDecision(
            bool changed,
            string reasonCode,
            float previousDifficulty,
            float nextDifficulty,
            float completionRatio,
            float averageReactionSec,
            float omittedRatio,
            float lateRatio)
        {
            var next = Mathf.Clamp01(nextDifficulty);
            var speed = Mathf.Lerp(_policy.minTargetSpeed, _policy.maxTargetSpeed, next);
            var scale = Mathf.Lerp(_policy.maxTargetScale, _policy.minTargetScale, next);
            var cueTimeoutSec = Mathf.Lerp(_policy.maxCueTimeoutSec, _policy.minCueTimeoutSec, next);

            return new DifficultyDecision
            {
                enabled = _policy.enabled,
                changed = changed,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode)
                    ? "KEEP_DIFFICULTY"
                    : reasonCode.Trim(),
                previousDifficulty = Mathf.Clamp01(previousDifficulty),
                nextDifficulty = next,
                delta = Mathf.Clamp(next - previousDifficulty, -1f, 1f),
                sensitivity = _policy.sensitivity,
                completionRatio = Mathf.Clamp01(completionRatio),
                averageReactionSec = Mathf.Max(0f, averageReactionSec),
                omittedRatio = Mathf.Clamp01(omittedRatio),
                lateRatio = Mathf.Clamp01(lateRatio),
                targetSpeed = Mathf.Max(0.05f, speed),
                targetScale = Mathf.Max(0.05f, scale),
                cueTimeoutSec = Mathf.Max(0.1f, cueTimeoutSec),
            };
        }

        private DifficultyPolicy NormalizePolicy(DifficultyPolicy policy)
        {
            var minSpeed = Mathf.Max(0.05f, policy.minTargetSpeed);
            var maxSpeed = Mathf.Max(minSpeed, policy.maxTargetSpeed);

            var minScale = Mathf.Max(0.05f, policy.minTargetScale);
            var maxScale = Mathf.Max(minScale, policy.maxTargetScale);

            var minCueTimeout = Mathf.Max(0.1f, policy.minCueTimeoutSec);
            var maxCueTimeout = Mathf.Max(minCueTimeout, policy.maxCueTimeoutSec);

            return new DifficultyPolicy
            {
                enabled = policy.enabled,
                sensitivity = Mathf.Clamp01(policy.sensitivity),
                minTargetSpeed = minSpeed,
                maxTargetSpeed = maxSpeed,
                minTargetScale = minScale,
                maxTargetScale = maxScale,
                minCueTimeoutSec = minCueTimeout,
                maxCueTimeoutSec = maxCueTimeout,
                promoteCompletionRatio = Mathf.Clamp01(policy.promoteCompletionRatio),
                demoteCompletionRatio = Mathf.Clamp01(policy.demoteCompletionRatio),
                promoteReactionSec = Mathf.Max(0.05f, policy.promoteReactionSec),
                demoteOmittedRatio = Mathf.Clamp01(policy.demoteOmittedRatio),
                demoteLateRatio = Mathf.Clamp01(policy.demoteLateRatio),
            };
        }

        private float ResolveStep()
        {
            return Mathf.Lerp(0.04f, 0.2f, _policy.sensitivity);
        }

        private static float ResolveRatio(int numerator, int denominator)
        {
            var safeNumerator = Mathf.Max(0, numerator);
            var safeDenominator = Mathf.Max(1, denominator);
            return Mathf.Clamp01((float)safeNumerator / safeDenominator);
        }
    }
}
