using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Shared feedback adapter for games (audio, hints, haptics stub).
    /// </summary>
    [DisallowMultipleComponent]
    public class GameFeedbackService : MonoBehaviour, GameContracts.IGameFeedback
    {
        [Serializable]
        private class AudioFeedbackEntry
        {
            public string id;
            public AudioClip clip;
        }

        [Header("Audio Feedback")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioFeedbackEntry[] _audioEntries = Array.Empty<AudioFeedbackEntry>();
        [SerializeField] private bool _logMissingAudioIds = true;

        [Header("Hints")]
        [SerializeField] private bool _logHints = true;

        private readonly Dictionary<string, AudioClip> _audioById =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }

            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 0f;
            }

            BuildAudioLookup();
        }

        public void PlaySfx(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            if (_audioById.TryGetValue(id, out var clip) && clip != null)
            {
                _audioSource.PlayOneShot(clip);
                return;
            }

            if (_logMissingAudioIds)
            {
                Logger.Warning($"[Feedback] Missing audio clip ID: {id}");
            }
        }

        public void HapticPulse(float amplitude, float durationSec)
        {
            // Platform-specific haptics are intentionally abstracted here.
            Logger.Debug($"[Feedback] Haptic pulse requested: amp={amplitude:F2}, dur={durationSec:F2}s");
        }

        public void ShowHint(string messageKey)
        {
            if (_logHints && !string.IsNullOrWhiteSpace(messageKey))
            {
                Logger.Info($"[Feedback] Hint: {messageKey}");
            }
        }

        private void BuildAudioLookup()
        {
            _audioById.Clear();

            foreach (var entry in _audioEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.clip == null)
                {
                    continue;
                }

                _audioById[entry.id] = entry.clip;
            }
        }
    }
}
