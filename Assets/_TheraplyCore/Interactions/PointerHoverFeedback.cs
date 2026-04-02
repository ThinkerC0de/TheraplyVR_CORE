using UnityEngine;

namespace TheraplyCore.Interactions
{
    public readonly struct PointerHoverFeedback
    {
        public PointerHoverFeedback(Color indicatorColor, bool isValidTarget)
        {
            IndicatorColor = indicatorColor;
            IsValidTarget = isValidTarget;
        }

        public Color IndicatorColor { get; }
        public bool IsValidTarget { get; }
    }

    public interface IPointerHoverFeedbackTarget
    {
        bool TryGetHoverFeedback(out PointerHoverFeedback feedback);
    }
}
