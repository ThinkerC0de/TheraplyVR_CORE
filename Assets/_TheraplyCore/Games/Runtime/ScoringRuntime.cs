using System;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Tracks score/lives and exposes adaptive difficulty state for flow runtime.
    /// </summary>
    public sealed class ScoringRuntime
    {
        public struct ScoringSnapshot
        {
            public int scoreTotal;
            public int correctCount;
            public int wrongCount;
            public int livesRemaining;
            public bool successThresholdReached;
            public bool adaptiveEnabled;
            public bool adaptiveChanged;
            public string adaptiveReasonCode;
            public float targetSpeed;
            public float targetScale;
            public float cueTimeoutSec;
            public float difficulty;
            public string adaptiveDifficultyState;
        }

        private readonly AdaptiveDifficultyController _adaptiveDifficultyController =
            new AdaptiveDifficultyController();

        private int _scoreTotal;
        private int _correctCount;
        private int _wrongCount;
        private int _livesRemaining;
        private int _targetSuccessCount;
        private int _pointsPerCorrect;
        private int _pointsPerWrong;

        private int _decisionCount;
        private int _omittedCount;
        private int _lateCount;
        private float _reactionSecSum;
        private int _reactionCount;

        private bool _adaptiveEnabled;
        private AdaptiveDifficultyController.DifficultyDecision _lastAdaptiveDecision;
        private bool _configured;

        public void Configure(GameContracts.GameDefinition definition)
        {
            var scoringPolicy = definition == null || definition.policies == null || definition.policies.scoringPolicy == null
                ? new GameContracts.ScoringPolicy()
                : definition.policies.scoringPolicy;

            _scoreTotal = 0;
            _correctCount = 0;
            _wrongCount = 0;
            _decisionCount = 0;
            _omittedCount = 0;
            _lateCount = 0;
            _reactionSecSum = 0f;
            _reactionCount = 0;

            _pointsPerCorrect = scoringPolicy.pointsPerCorrect;
            _pointsPerWrong = scoringPolicy.pointsPerWrong;
            _livesRemaining = Mathf.Max(0, scoringPolicy.lives);

            var targetCount = definition == null || definition.config == null
                ? 1
                : Mathf.Max(1, definition.config.targetCount);
            _targetSuccessCount = targetCount;

            var adaptivePolicy = AdaptiveDifficultyController.CreateDefaultPolicy();
            _adaptiveEnabled = definition != null &&
                               definition.policies != null &&
                               definition.policies.difficultyPolicy != null &&
                               definition.policies.difficultyPolicy.adaptiveEnabled;
            adaptivePolicy.enabled = _adaptiveEnabled;
            _adaptiveDifficultyController.Configure(adaptivePolicy, startingDifficulty: 0.5f);
            _lastAdaptiveDecision = _adaptiveDifficultyController.CurrentDecision;

            _configured = true;
        }

        public void RecordActionDecision(bool accepted, string reasonCode, float reactionSec)
        {
            EnsureConfigured();

            _decisionCount++;
            if (accepted)
            {
                _correctCount++;
                _scoreTotal += _pointsPerCorrect;
            }
            else
            {
                _wrongCount++;
                _scoreTotal += _pointsPerWrong;
                if (_livesRemaining > 0)
                {
                    _livesRemaining--;
                }
            }

            if (!string.IsNullOrWhiteSpace(reasonCode))
            {
                var normalizedReason = reasonCode.Trim();
                if (normalizedReason.IndexOf("OMITTED", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _omittedCount++;
                }

                if (normalizedReason.IndexOf("LATE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lateCount++;
                }
            }

            var safeReactionSec = Mathf.Max(0f, reactionSec);
            if (safeReactionSec > 0.0001f)
            {
                _reactionSecSum += safeReactionSec;
                _reactionCount++;
            }

            var summary = new TaskOutcomeAggregator.TaskOutcomeSummary
            {
                cuesPresented = Mathf.Max(1, _decisionCount),
                actionsObserved = _decisionCount,
                correctCount = _correctCount,
                incorrectCount = _wrongCount,
                lateCount = _lateCount,
                omittedCount = _omittedCount,
                completionRatio = Mathf.Clamp01((float)_correctCount / Mathf.Max(1, _decisionCount)),
                averageReactionSec = _reactionCount > 0
                    ? Mathf.Max(0f, _reactionSecSum / _reactionCount)
                    : 1f,
            };

            _lastAdaptiveDecision = _adaptiveDifficultyController.Evaluate(summary);
        }

        public ScoringSnapshot GetSnapshot()
        {
            EnsureConfigured();

            var successReached = _correctCount >= Mathf.Max(1, _targetSuccessCount);
            var adaptiveState = BuildAdaptiveStateText(_lastAdaptiveDecision);

            return new ScoringSnapshot
            {
                scoreTotal = _scoreTotal,
                correctCount = _correctCount,
                wrongCount = _wrongCount,
                livesRemaining = _livesRemaining,
                successThresholdReached = successReached,
                adaptiveEnabled = _adaptiveEnabled,
                adaptiveChanged = _lastAdaptiveDecision.changed,
                adaptiveReasonCode = NormalizeOrFallback(_lastAdaptiveDecision.reasonCode, "KEEP_DIFFICULTY"),
                targetSpeed = _lastAdaptiveDecision.targetSpeed,
                targetScale = _lastAdaptiveDecision.targetScale,
                cueTimeoutSec = _lastAdaptiveDecision.cueTimeoutSec,
                difficulty = _lastAdaptiveDecision.nextDifficulty,
                adaptiveDifficultyState = adaptiveState,
            };
        }

        private void EnsureConfigured()
        {
            if (_configured)
            {
                return;
            }

            Configure(null);
        }

        private static string BuildAdaptiveStateText(AdaptiveDifficultyController.DifficultyDecision decision)
        {
            return
                "difficulty=" +
                decision.nextDifficulty.ToString("F3") +
                ";speed=" +
                decision.targetSpeed.ToString("F3") +
                ";scale=" +
                decision.targetScale.ToString("F3") +
                ";timeout=" +
                decision.cueTimeoutSec.ToString("F3") +
                ";reason=" +
                NormalizeOrFallback(decision.reasonCode, "KEEP_DIFFICULTY");
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
