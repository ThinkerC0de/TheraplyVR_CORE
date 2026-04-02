using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Non-blocking cue scheduler for task stimuli.
    /// </summary>
    public sealed class StimulusScheduler
    {
        public struct ScheduledCue
        {
            public string taskRunId;
            public string stepId;
            public string cueId;
            public string stimulusId;
            public string stimulusChannel;
            public string expectedTargetId;
            public string requiredAction;
            public float dueAtElapsedSec;
            public float cueTimeoutSec;
        }

        private sealed class ScheduledCueState
        {
            public ScheduledCue cue;
            public bool dispatched;
        }

        private readonly List<ScheduledCueState> _scheduled = new List<ScheduledCueState>(32);

        public int PendingCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _scheduled.Count; i++)
                {
                    var item = _scheduled[i];
                    if (item != null && !item.dispatched)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Reset()
        {
            _scheduled.Clear();
        }

        public void ScheduleCue(ScheduledCue cue)
        {
            var normalizedCue = cue;
            normalizedCue.taskRunId = NormalizeOrFallback(normalizedCue.taskRunId, string.Empty);
            normalizedCue.stepId = NormalizeOrFallback(
                normalizedCue.stepId,
                "step_" + (_scheduled.Count + 1).ToString(CultureInfo.InvariantCulture));
            normalizedCue.cueId = NormalizeOrFallback(normalizedCue.cueId, Guid.NewGuid().ToString());
            normalizedCue.stimulusId = NormalizeOrFallback(normalizedCue.stimulusId, "TASK_STIMULUS");
            normalizedCue.stimulusChannel = NormalizeOrFallback(normalizedCue.stimulusChannel, "VISUAL");
            normalizedCue.expectedTargetId = NormalizeOrFallback(normalizedCue.expectedTargetId, string.Empty);
            normalizedCue.requiredAction = NormalizeOrFallback(normalizedCue.requiredAction, "SELECT");
            normalizedCue.dueAtElapsedSec = Mathf.Max(0f, normalizedCue.dueAtElapsedSec);
            normalizedCue.cueTimeoutSec = Mathf.Max(0.01f, normalizedCue.cueTimeoutSec);

            _scheduled.Add(new ScheduledCueState
            {
                cue = normalizedCue,
                dispatched = false,
            });
        }

        public int CollectDueCues(float elapsedSec, List<ScheduledCue> output)
        {
            if (output == null)
            {
                return 0;
            }

            var now = Mathf.Max(0f, elapsedSec);
            var count = 0;

            for (var i = 0; i < _scheduled.Count; i++)
            {
                var scheduled = _scheduled[i];
                if (scheduled == null || scheduled.dispatched)
                {
                    continue;
                }

                if (scheduled.cue.dueAtElapsedSec > now)
                {
                    continue;
                }

                scheduled.dispatched = true;
                output.Add(scheduled.cue);
                count++;
            }

            return count;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
