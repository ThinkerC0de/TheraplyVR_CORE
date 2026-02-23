using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    public enum SequenceOutcomeType
    {
        Observed = 0,
        Correct = 1,
        Incorrect = 2,
        Late = 3,
        Redundant = 4,
        Omitted = 5,
    }

    /// <summary>
    /// Lightweight state engine for sequential task cues and outcomes.
    /// </summary>
    public sealed class SequenceTaskEngine
    {
        public struct SequenceCue
        {
            public string taskRunId;
            public string stepId;
            public string cueId;
            public string expectedTargetId;
            public string requiredAction;
            public float cueAtElapsedSec;
            public float cueTimeoutSec;
        }

        public struct SequenceAction
        {
            public string stepId;
            public string cueId;
            public string expectedTargetId;
            public string targetId;
            public string inputSource;
            public float actionAtElapsedSec;
        }

        public struct SequenceOutcome
        {
            public string taskRunId;
            public string stepId;
            public string cueId;
            public string expectedTargetId;
            public string actualTargetId;
            public string requiredAction;
            public string inputSource;
            public string reasonCode;
            public SequenceOutcomeType outcomeType;
            public float cueAtElapsedSec;
            public float actionAtElapsedSec;
            public float reactionSec;
            public float timeoutSec;
            public bool isResolvedStep;
        }

        private sealed class SequenceStepState
        {
            public string taskRunId = string.Empty;
            public string stepId = string.Empty;
            public string cueId = string.Empty;
            public string expectedTargetId = string.Empty;
            public string requiredAction = "SELECT";
            public float cueAtElapsedSec;
            public float cueTimeoutSec;
            public bool resolved;
            public bool timeoutEmitted;
        }

        private readonly List<SequenceStepState> _steps = new List<SequenceStepState>(32);
        private string _taskRunId = string.Empty;

        public string TaskRunId => _taskRunId;
        public int RegisteredSteps => _steps.Count;

        public void Reset(string taskRunId)
        {
            _taskRunId = NormalizeOrFallback(taskRunId, Guid.NewGuid().ToString());
            _steps.Clear();
        }

        public void RegisterCue(SequenceCue cue)
        {
            if (string.IsNullOrWhiteSpace(_taskRunId))
            {
                Reset(cue.taskRunId);
            }

            var stepState = new SequenceStepState
            {
                taskRunId = NormalizeOrFallback(cue.taskRunId, _taskRunId),
                stepId = NormalizeOrFallback(cue.stepId, "step_" + (_steps.Count + 1).ToString(CultureInfo.InvariantCulture)),
                cueId = NormalizeOrFallback(cue.cueId, Guid.NewGuid().ToString()),
                expectedTargetId = NormalizeOrFallback(cue.expectedTargetId, string.Empty),
                requiredAction = NormalizeOrFallback(cue.requiredAction, "SELECT"),
                cueAtElapsedSec = Mathf.Max(0f, cue.cueAtElapsedSec),
                cueTimeoutSec = Mathf.Max(0.01f, cue.cueTimeoutSec),
                resolved = false,
                timeoutEmitted = false,
            };

            _steps.Add(stepState);
        }

        public bool TryRecordAction(SequenceAction action, out SequenceOutcome outcome)
        {
            var pendingStep = FindNextPendingStep();
            if (pendingStep == null)
            {
                outcome = CreateOutcome(
                    outcomeType: SequenceOutcomeType.Redundant,
                    reasonCode: "NO_PENDING_STEP",
                    step: null,
                    action: action,
                    isResolvedStep: false);
                return true;
            }

            var expectedTarget = NormalizeOrFallback(
                action.expectedTargetId,
                pendingStep.expectedTargetId);
            var actualTarget = NormalizeOrFallback(action.targetId, string.Empty);
            var reactionSec = Mathf.Max(0f, action.actionAtElapsedSec - pendingStep.cueAtElapsedSec);
            var timeoutSec = Mathf.Max(0.01f, pendingStep.cueTimeoutSec);
            var targetMatches = !string.IsNullOrWhiteSpace(expectedTarget) &&
                                string.Equals(expectedTarget, actualTarget, StringComparison.OrdinalIgnoreCase);

            if (!targetMatches)
            {
                outcome = CreateOutcome(
                    outcomeType: SequenceOutcomeType.Incorrect,
                    reasonCode: "TARGET_MISMATCH",
                    step: pendingStep,
                    action: action,
                    isResolvedStep: false);
                return true;
            }

            if (reactionSec > timeoutSec)
            {
                pendingStep.resolved = true;
                pendingStep.timeoutEmitted = true;
                outcome = CreateOutcome(
                    outcomeType: SequenceOutcomeType.Late,
                    reasonCode: "ACTION_AFTER_TIMEOUT",
                    step: pendingStep,
                    action: action,
                    isResolvedStep: true);
                return true;
            }

            pendingStep.resolved = true;
            pendingStep.timeoutEmitted = true;
            outcome = CreateOutcome(
                outcomeType: SequenceOutcomeType.Correct,
                reasonCode: "TARGET_MATCHED",
                step: pendingStep,
                action: action,
                isResolvedStep: true);
            return true;
        }

        public int CollectTimeoutOutcomes(float elapsedSec, List<SequenceOutcome> output)
        {
            if (output == null)
            {
                return 0;
            }

            var now = Mathf.Max(0f, elapsedSec);
            var emitted = 0;

            for (var i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                if (step == null || step.resolved || step.timeoutEmitted)
                {
                    continue;
                }

                if (now - step.cueAtElapsedSec <= Mathf.Max(0.01f, step.cueTimeoutSec))
                {
                    continue;
                }

                step.resolved = true;
                step.timeoutEmitted = true;

                output.Add(new SequenceOutcome
                {
                    taskRunId = step.taskRunId,
                    stepId = step.stepId,
                    cueId = step.cueId,
                    expectedTargetId = step.expectedTargetId,
                    actualTargetId = string.Empty,
                    requiredAction = step.requiredAction,
                    inputSource = string.Empty,
                    reasonCode = "STEP_TIMEOUT",
                    outcomeType = SequenceOutcomeType.Omitted,
                    cueAtElapsedSec = step.cueAtElapsedSec,
                    actionAtElapsedSec = now,
                    reactionSec = Mathf.Max(0f, now - step.cueAtElapsedSec),
                    timeoutSec = Mathf.Max(0.01f, step.cueTimeoutSec),
                    isResolvedStep = true,
                });
                emitted++;
            }

            return emitted;
        }

        private SequenceStepState FindNextPendingStep()
        {
            for (var i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                if (step != null && !step.resolved)
                {
                    return step;
                }
            }

            return null;
        }

        private SequenceOutcome CreateOutcome(
            SequenceOutcomeType outcomeType,
            string reasonCode,
            SequenceStepState step,
            SequenceAction action,
            bool isResolvedStep)
        {
            var safeStepId = step == null
                ? NormalizeOrFallback(action.stepId, string.Empty)
                : step.stepId;
            var safeCueId = step == null
                ? NormalizeOrFallback(action.cueId, string.Empty)
                : step.cueId;
            var expectedTargetId = step == null
                ? NormalizeOrFallback(action.expectedTargetId, string.Empty)
                : step.expectedTargetId;
            var cueAt = step == null
                ? Mathf.Max(0f, action.actionAtElapsedSec)
                : step.cueAtElapsedSec;
            var timeout = step == null
                ? 0f
                : Mathf.Max(0.01f, step.cueTimeoutSec);

            return new SequenceOutcome
            {
                taskRunId = NormalizeOrFallback(
                    step == null ? _taskRunId : step.taskRunId,
                    _taskRunId),
                stepId = safeStepId,
                cueId = safeCueId,
                expectedTargetId = expectedTargetId,
                actualTargetId = NormalizeOrFallback(action.targetId, string.Empty),
                requiredAction = step == null
                    ? "SELECT"
                    : NormalizeOrFallback(step.requiredAction, "SELECT"),
                inputSource = NormalizeOrFallback(action.inputSource, string.Empty),
                reasonCode = NormalizeOrFallback(reasonCode, "ACTION_OBSERVED"),
                outcomeType = outcomeType,
                cueAtElapsedSec = cueAt,
                actionAtElapsedSec = Mathf.Max(0f, action.actionAtElapsedSec),
                reactionSec = Mathf.Max(0f, action.actionAtElapsedSec - cueAt),
                timeoutSec = timeout,
                isResolvedStep = isResolvedStep,
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
