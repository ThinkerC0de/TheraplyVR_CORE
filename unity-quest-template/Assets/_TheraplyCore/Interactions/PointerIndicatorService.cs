using UnityEngine;

namespace TheraplyCore.Interactions
{
    public enum PointerHitFeedbackState
    {
        None = 0,
        Correct = 1,
        Incorrect = 2,
    }

    [DisallowMultipleComponent]
    public sealed class PointerIndicatorService : MonoBehaviour
    {
        [Header("Feedback Colors")]
        [SerializeField] private Color _defaultColor = new Color(0.85f, 0.9f, 1f, 1f);
        [SerializeField] private Color _correctHitColor = new Color(0.2f, 1f, 0.45f, 1f);
        [SerializeField] private Color _incorrectHitColor = new Color(1f, 0.3f, 0.3f, 1f);
        [SerializeField] private float _feedbackDurationSeconds = 0.2f;

        public static PointerIndicatorService Instance { get; private set; }

        private bool _hasTargetColor;
        private Color _targetColor;
        private PointerHitFeedbackState _feedbackState = PointerHitFeedbackState.None;
        private float _feedbackUntilRealtime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<PointerIndicatorService>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("PointerIndicatorService");
            host.AddComponent<PointerIndicatorService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetTargetColor(Color color)
        {
            _targetColor = color;
            _hasTargetColor = true;
        }

        public void ClearTargetColor()
        {
            _hasTargetColor = false;
        }

        public void ReportHit(bool isCorrect)
        {
            _feedbackState = isCorrect ? PointerHitFeedbackState.Correct : PointerHitFeedbackState.Incorrect;
            _feedbackUntilRealtime = Time.realtimeSinceStartup + Mathf.Max(0.05f, _feedbackDurationSeconds);
        }

        public Color ResolveIndicatorColor(Color fallback)
        {
            RefreshFeedbackTimeout();

            switch (_feedbackState)
            {
                case PointerHitFeedbackState.Correct:
                    return _correctHitColor;
                case PointerHitFeedbackState.Incorrect:
                    return _incorrectHitColor;
            }

            if (_hasTargetColor)
            {
                return _targetColor;
            }

            if (_defaultColor.a > 0f)
            {
                return _defaultColor;
            }

            return fallback;
        }

        private void RefreshFeedbackTimeout()
        {
            if (_feedbackState == PointerHitFeedbackState.None)
            {
                return;
            }

            if (Time.realtimeSinceStartup >= _feedbackUntilRealtime)
            {
                _feedbackState = PointerHitFeedbackState.None;
            }
        }
    }
}
