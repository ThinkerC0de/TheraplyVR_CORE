using System;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Loads and validates GameDefinition from ScriptableObject or JSON text.
    /// This runtime provider is framework-level and does not include game-specific branching.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlowConfigProvider : MonoBehaviour, GameContracts.IGameDefinitionProvider
    {
        [Header("Definition Sources")]
        [SerializeField] private GameContracts.GameDefinitionAsset _definitionAsset;
        [SerializeField] private TextAsset _definitionJson;
        [SerializeField] private bool _preferJsonSource;
        [SerializeField] private bool _fallbackToSampleWhenMissing;

        [Header("Validation")]
        [SerializeField] private bool _validateOnAwake = true;
        [SerializeField] private bool _logValidationResult;

        private GameContracts.GameDefinition _cachedDefinition;
        private string _lastReasonCode = string.Empty;
        private bool _isLoaded;

        public bool IsLoaded => _isLoaded;
        public string LastReasonCode => _lastReasonCode ?? string.Empty;

        private void Awake()
        {
            if (_validateOnAwake)
            {
                TryGetGameDefinition(out _, out _);
            }
        }

        public bool TryGetGameDefinition(out GameContracts.GameDefinition definition, out string reasonCode)
        {
            if (_isLoaded && _cachedDefinition != null)
            {
                definition = CloneDefinition(_cachedDefinition);
                reasonCode = string.Empty;
                return true;
            }

            if (!TryLoadDefinition(out var loadedDefinition, out reasonCode))
            {
                definition = null;
                _isLoaded = false;
                _cachedDefinition = null;
                _lastReasonCode = NormalizeReason(reasonCode);
                MaybeLogResult(false, _lastReasonCode);
                return false;
            }

            if (!GameContracts.SessionFlowDefinitionValidator.TryValidate(loadedDefinition, out reasonCode))
            {
                definition = null;
                _isLoaded = false;
                _cachedDefinition = null;
                _lastReasonCode = NormalizeReason(reasonCode);
                MaybeLogResult(false, _lastReasonCode);
                return false;
            }

            _cachedDefinition = CloneDefinition(loadedDefinition);
            _isLoaded = true;
            _lastReasonCode = string.Empty;

            definition = CloneDefinition(_cachedDefinition);
            reasonCode = string.Empty;
            MaybeLogResult(true, "DEFINITION_READY");
            return true;
        }

        public void InvalidateCache()
        {
            _isLoaded = false;
            _cachedDefinition = null;
            _lastReasonCode = string.Empty;
        }

        private bool TryLoadDefinition(out GameContracts.GameDefinition definition, out string reasonCode)
        {
            definition = null;
            reasonCode = string.Empty;

            if (_preferJsonSource)
            {
                if (TryLoadFromJson(out definition, out reasonCode))
                {
                    return true;
                }

                if (TryLoadFromAsset(out definition, out _))
                {
                    reasonCode = string.Empty;
                    return true;
                }
            }
            else
            {
                if (TryLoadFromAsset(out definition, out reasonCode))
                {
                    return true;
                }

                if (TryLoadFromJson(out definition, out _))
                {
                    reasonCode = string.Empty;
                    return true;
                }
            }

            if (_fallbackToSampleWhenMissing)
            {
                definition = GameContracts.GameDefinition.CreateSample();
                reasonCode = string.Empty;
                return true;
            }

            reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.DefinitionSourceMissing;
            return false;
        }

        private bool TryLoadFromAsset(out GameContracts.GameDefinition definition, out string reasonCode)
        {
            definition = null;
            reasonCode = string.Empty;

            if (_definitionAsset == null || _definitionAsset.definition == null)
            {
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.DefinitionSourceMissing;
                return false;
            }

            definition = CloneDefinition(_definitionAsset.definition);
            return definition != null;
        }

        private bool TryLoadFromJson(out GameContracts.GameDefinition definition, out string reasonCode)
        {
            definition = null;
            reasonCode = string.Empty;

            if (_definitionJson == null || string.IsNullOrWhiteSpace(_definitionJson.text))
            {
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.DefinitionSourceMissing;
                return false;
            }

            try
            {
                definition = JsonUtility.FromJson<GameContracts.GameDefinition>(_definitionJson.text);
                if (definition == null)
                {
                    reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.DefinitionParseFailed;
                    return false;
                }
            }
            catch (Exception)
            {
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.DefinitionParseFailed;
                return false;
            }

            return true;
        }

        private static GameContracts.GameDefinition CloneDefinition(GameContracts.GameDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            var json = JsonUtility.ToJson(source);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonUtility.FromJson<GameContracts.GameDefinition>(json);
        }

        private void MaybeLogResult(bool success, string code)
        {
            if (!_logValidationResult)
            {
                return;
            }

            if (success)
            {
                Logger.Info("[FlowConfigProvider] Definition loaded and validated.");
            }
            else
            {
                Logger.Warning("[FlowConfigProvider] Definition load/validate failed. reasonCode=" + NormalizeReason(code));
            }
        }

        private static string NormalizeReason(string reasonCode)
        {
            return string.IsNullOrWhiteSpace(reasonCode)
                ? GameContracts.SessionFlowDefinitionReasonCodes.DefinitionParseFailed
                : reasonCode.Trim();
        }
    }
}
