using System;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Aggregates per-task run outcomes into a canonical summary payload.
    /// </summary>
    public sealed class TaskOutcomeAggregator
    {
        public struct TaskOutcomeSummary
        {
            public string taskRunId;
            public string gameId;
            public int cuesPresented;
            public int actionsObserved;
            public int correctCount;
            public int incorrectCount;
            public int lateCount;
            public int omittedCount;
            public int redundantCount;
            public float firstActionLatencySec;
            public float averageReactionSec;
            public float completionRatio;
            public float elapsedSec;
        }

        private string _taskRunId = string.Empty;
        private string _gameId = string.Empty;
        private int _cuesPresented;
        private int _actionsObserved;
        private int _correctCount;
        private int _incorrectCount;
        private int _lateCount;
        private int _omittedCount;
        private int _redundantCount;
        private float _firstActionLatencySec = -1f;
        private float _reactionSecSum;
        private int _reactionCount;

        public void Reset(string taskRunId, string gameId)
        {
            _taskRunId = NormalizeOrFallback(taskRunId, Guid.NewGuid().ToString());
            _gameId = NormalizeOrFallback(gameId, string.Empty);
            _cuesPresented = 0;
            _actionsObserved = 0;
            _correctCount = 0;
            _incorrectCount = 0;
            _lateCount = 0;
            _omittedCount = 0;
            _redundantCount = 0;
            _firstActionLatencySec = -1f;
            _reactionSecSum = 0f;
            _reactionCount = 0;
        }

        public void RecordCue(StimulusScheduler.ScheduledCue cue)
        {
            _cuesPresented++;
            if (string.IsNullOrWhiteSpace(_taskRunId) && !string.IsNullOrWhiteSpace(cue.taskRunId))
            {
                _taskRunId = cue.taskRunId.Trim();
            }
        }

        public void RecordOutcome(SequenceTaskEngine.SequenceOutcome outcome)
        {
            _actionsObserved++;

            switch (outcome.outcomeType)
            {
                case SequenceOutcomeType.Correct:
                    _correctCount++;
                    break;
                case SequenceOutcomeType.Incorrect:
                    _incorrectCount++;
                    break;
                case SequenceOutcomeType.Late:
                    _lateCount++;
                    break;
                case SequenceOutcomeType.Omitted:
                    _omittedCount++;
                    break;
                case SequenceOutcomeType.Redundant:
                    _redundantCount++;
                    break;
            }

            var reaction = Mathf.Max(0f, outcome.reactionSec);
            if (_firstActionLatencySec < 0f)
            {
                _firstActionLatencySec = reaction;
            }

            if (outcome.outcomeType != SequenceOutcomeType.Redundant)
            {
                _reactionSecSum += reaction;
                _reactionCount++;
            }
        }

        public TaskOutcomeSummary BuildSummary(float elapsedSec)
        {
            var solvedSteps = _correctCount + _lateCount;
            var denominator = Mathf.Max(1, _cuesPresented);
            var averageReaction = _reactionCount > 0
                ? Mathf.Max(0f, _reactionSecSum / _reactionCount)
                : 0f;

            return new TaskOutcomeSummary
            {
                taskRunId = NormalizeOrFallback(_taskRunId, string.Empty),
                gameId = NormalizeOrFallback(_gameId, string.Empty),
                cuesPresented = _cuesPresented,
                actionsObserved = _actionsObserved,
                correctCount = _correctCount,
                incorrectCount = _incorrectCount,
                lateCount = _lateCount,
                omittedCount = _omittedCount,
                redundantCount = _redundantCount,
                firstActionLatencySec = _firstActionLatencySec < 0f ? 0f : _firstActionLatencySec,
                averageReactionSec = averageReaction,
                completionRatio = Mathf.Clamp01((float)solvedSteps / denominator),
                elapsedSec = Mathf.Max(0f, elapsedSec),
            };
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
