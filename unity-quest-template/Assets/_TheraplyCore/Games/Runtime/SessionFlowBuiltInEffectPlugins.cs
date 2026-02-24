using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Built-in effect plugins for common scene/audio/ui actions.
    /// </summary>
    public static class SessionFlowBuiltInEffectPlugins
    {
        private delegate bool EffectHandler(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode);

        private sealed class DelegateEffectPlugin : IEffectPlugin
        {
            private readonly string _effectId;
            private readonly EffectHandler _handler;

            public DelegateEffectPlugin(string effectId, EffectHandler handler)
            {
                _effectId = string.IsNullOrWhiteSpace(effectId) ? string.Empty : effectId.Trim();
                _handler = handler;
            }

            public string EffectId => _effectId;

            public bool Execute(
                GameContracts.EffectDefinition effect,
                EffectExecutionContext context,
                EffectRuntimeServices services,
                out string reasonCode)
            {
                if (_handler == null)
                {
                    reasonCode = "EFFECT_HANDLER_MISSING";
                    return false;
                }

                return _handler(effect, context, services, out reasonCode);
            }
        }

        public static void RegisterBuiltIns(EffectPluginRegistry registry, bool replaceExisting = true)
        {
            if (registry == null)
            {
                return;
            }

            registry.Register(new DelegateEffectPlugin("show_object", ExecuteShowObject), replaceExisting);
            registry.Register(new DelegateEffectPlugin("hide_object", ExecuteHideObject), replaceExisting);
            registry.Register(new DelegateEffectPlugin("spawn_object", ExecuteSpawnObject), replaceExisting);
            registry.Register(new DelegateEffectPlugin("despawn_object", ExecuteDespawnObject), replaceExisting);
            registry.Register(new DelegateEffectPlugin("play_audio", ExecutePlayAudio), replaceExisting);
            registry.Register(new DelegateEffectPlugin("stop_audio", ExecuteStopAudio), replaceExisting);
            registry.Register(new DelegateEffectPlugin("play_sfx", ExecutePlaySfx), replaceExisting);
            registry.Register(new DelegateEffectPlugin("play_vfx", ExecutePlayVfx), replaceExisting);
            registry.Register(new DelegateEffectPlugin("set_animator_trigger", ExecuteSetAnimatorTrigger), replaceExisting);
            registry.Register(new DelegateEffectPlugin("play_timeline", ExecutePlayTimeline), replaceExisting);
            registry.Register(new DelegateEffectPlugin("stop_timeline", ExecuteStopTimeline), replaceExisting);
            registry.Register(new DelegateEffectPlugin("enable_interaction", ExecuteEnableInteraction), replaceExisting);
            registry.Register(new DelegateEffectPlugin("disable_interaction", ExecuteDisableInteraction), replaceExisting);
            registry.Register(new DelegateEffectPlugin("set_ui_text", ExecuteSetUiText), replaceExisting);
            registry.Register(new DelegateEffectPlugin("update_score", ExecuteUpdateScore), replaceExisting);
            registry.Register(new DelegateEffectPlugin("fade_screen", ExecuteFadeScreen), replaceExisting);
            registry.Register(new DelegateEffectPlugin("teleport_actor", ExecuteTeleportActor), replaceExisting);
            registry.Register(new DelegateEffectPlugin("emit_hint", ExecuteEmitHint), replaceExisting);
        }

        private static bool ExecuteShowObject(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            target.SetActive(true);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteHideObject(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            target.SetActive(false);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteSpawnObject(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (services == null || services.sceneRuntime == null)
            {
                reasonCode = "SCENE_RUNTIME_MISSING";
                return false;
            }

            var prefabKey = ReadParameter(effect, "prefabKey", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(prefabKey))
            {
                reasonCode = "PREFAB_KEY_REQUIRED";
                return false;
            }

            var bindingKey = ReadParameter(effect, "bindingKey", effect == null ? string.Empty : effect.binding);
            var spawnPointKey = ReadParameter(effect, "spawnPointKey", string.Empty);
            return services.sceneRuntime.TrySpawn(bindingKey, prefabKey, spawnPointKey, out _, out reasonCode);
        }

        private static bool ExecuteDespawnObject(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (services == null || services.sceneRuntime == null)
            {
                reasonCode = "SCENE_RUNTIME_MISSING";
                return false;
            }

            var instanceId = ReadParameter(effect, "instanceId", string.Empty);
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                return services.sceneRuntime.TryDespawnByInstanceId(instanceId, out reasonCode);
            }

            var bindingKey = ReadParameter(effect, "bindingKey", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(bindingKey))
            {
                reasonCode = "BINDING_KEY_REQUIRED";
                return false;
            }

            return services.sceneRuntime.TryDespawnByBindingKey(bindingKey, out reasonCode);
        }

        private static bool ExecutePlayAudio(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveAudio(effect, services, out var audioSource, out reasonCode))
            {
                return false;
            }

            audioSource.loop = ReadBoolParameter(effect, "loop", audioSource.loop);
            audioSource.Play();
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteStopAudio(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveAudio(effect, services, out var audioSource, out reasonCode))
            {
                return false;
            }

            audioSource.Stop();
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecutePlaySfx(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (services == null || services.feedback == null)
            {
                reasonCode = "FEEDBACK_SERVICE_MISSING";
                return false;
            }

            var sfxId = ReadParameter(effect, "sfxId", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(sfxId))
            {
                reasonCode = "SFX_ID_REQUIRED";
                return false;
            }

            services.feedback.PlaySfx(sfxId);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecutePlayVfx(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            var played = false;
            var particleSystems = target.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            for (var i = 0; i < particleSystems.Length; i++)
            {
                var particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                if (!particleSystem.gameObject.activeSelf)
                {
                    particleSystem.gameObject.SetActive(true);
                }

                particleSystem.Play();
                played = true;
            }

            if (!played)
            {
                target.SetActive(true);
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteSetAnimatorTrigger(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            var trigger = ReadParameter(effect, "trigger", string.Empty);
            if (string.IsNullOrWhiteSpace(trigger))
            {
                reasonCode = "ANIMATOR_TRIGGER_REQUIRED";
                return false;
            }

            var animator = target.GetComponent<Animator>();
            if (animator == null)
            {
                animator = target.GetComponentInChildren<Animator>(includeInactive: true);
            }

            if (animator == null)
            {
                reasonCode = "ANIMATOR_MISSING";
                return false;
            }

            animator.SetTrigger(trigger.Trim());
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecutePlayTimeline(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveTimeline(effect, services, out var timeline, out reasonCode))
            {
                return false;
            }

            timeline.Play();
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteStopTimeline(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveTimeline(effect, services, out var timeline, out reasonCode))
            {
                return false;
            }

            timeline.Stop();
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteEnableInteraction(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            SetInteractionEnabled(target, enabled: true);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteDisableInteraction(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            SetInteractionEnabled(target, enabled: false);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteSetUiText(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            var text = ReadParameter(effect, "text", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = ReadParameter(effect, "value", string.Empty);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                reasonCode = "UI_TEXT_REQUIRED";
                return false;
            }

            if (TrySetKnownTextComponent(target, text.Trim(), out reasonCode))
            {
                return true;
            }

            reasonCode = "UI_TEXT_COMPONENT_MISSING";
            return false;
        }

        private static bool ExecuteUpdateScore(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (effect != null && !string.IsNullOrWhiteSpace(effect.binding) &&
                services != null && services.bindings != null &&
                services.bindings.TryGetObject(effect.binding.Trim(), out var scoreObject) &&
                scoreObject != null)
            {
                var scoreText = ReadParameter(effect, "text", string.Empty);
                if (string.IsNullOrWhiteSpace(scoreText))
                {
                    scoreText = ReadParameter(effect, "value", string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(scoreText))
                {
                    TrySetKnownTextComponent(scoreObject, scoreText.Trim(), out _);
                }
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteFadeScreen(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (!TryResolveObject(effect, services, out var target, out reasonCode))
            {
                return false;
            }

            var alpha = ReadFloatParameter(effect, "alpha", 1f);
            var clampedAlpha = Clamp01(alpha);

            var canvasGroup = target.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = clampedAlpha;
                reasonCode = string.Empty;
                return true;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.material == null || !renderer.material.HasProperty("_Color"))
                {
                    continue;
                }

                var color = renderer.material.color;
                color.a = clampedAlpha;
                renderer.material.color = color;
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteTeleportActor(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (services == null || services.sceneRuntime == null)
            {
                reasonCode = "SCENE_RUNTIME_MISSING";
                return false;
            }

            var anchorKey = ReadParameter(effect, "anchorKey", effect == null ? string.Empty : effect.binding);
            var poseKey = ReadParameter(effect, "poseKey", string.Empty);
            if (string.IsNullOrWhiteSpace(anchorKey) || string.IsNullOrWhiteSpace(poseKey))
            {
                reasonCode = "ANCHOR_OR_POSE_KEY_REQUIRED";
                return false;
            }

            return services.sceneRuntime.TryTeleportAnchor(anchorKey, poseKey, out reasonCode);
        }

        private static bool ExecuteEmitHint(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            if (services == null || services.feedback == null)
            {
                reasonCode = "FEEDBACK_SERVICE_MISSING";
                return false;
            }

            var messageKey = ReadParameter(effect, "messageKey", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(messageKey))
            {
                reasonCode = "HINT_KEY_REQUIRED";
                return false;
            }

            services.feedback.ShowHint(messageKey.Trim());
            reasonCode = string.Empty;
            return true;
        }

        private static bool TryResolveObject(
            GameContracts.EffectDefinition effect,
            EffectRuntimeServices services,
            out GameObject target,
            out string reasonCode)
        {
            target = null;
            if (services == null || services.bindings == null)
            {
                reasonCode = "FLOW_BINDINGS_MISSING";
                return false;
            }

            if (effect == null || string.IsNullOrWhiteSpace(effect.binding))
            {
                reasonCode = "EFFECT_BINDING_REQUIRED";
                return false;
            }

            if (!services.bindings.TryGetObject(effect.binding.Trim(), out target) || target == null)
            {
                reasonCode = "EFFECT_BINDING_NOT_FOUND";
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool TryResolveAudio(
            GameContracts.EffectDefinition effect,
            EffectRuntimeServices services,
            out AudioSource audioSource,
            out string reasonCode)
        {
            audioSource = null;
            if (services == null || services.bindings == null)
            {
                reasonCode = "FLOW_BINDINGS_MISSING";
                return false;
            }

            if (effect == null || string.IsNullOrWhiteSpace(effect.binding))
            {
                reasonCode = "EFFECT_BINDING_REQUIRED";
                return false;
            }

            if (!services.bindings.TryGetAudio(effect.binding.Trim(), out audioSource) || audioSource == null)
            {
                reasonCode = "AUDIO_BINDING_NOT_FOUND";
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool TryResolveTimeline(
            GameContracts.EffectDefinition effect,
            EffectRuntimeServices services,
            out PlayableDirector timeline,
            out string reasonCode)
        {
            timeline = null;
            if (services == null || services.bindings == null)
            {
                reasonCode = "FLOW_BINDINGS_MISSING";
                return false;
            }

            if (effect == null || string.IsNullOrWhiteSpace(effect.binding))
            {
                reasonCode = "EFFECT_BINDING_REQUIRED";
                return false;
            }

            if (!services.bindings.TryGetTimeline(effect.binding.Trim(), out timeline) || timeline == null)
            {
                reasonCode = "TIMELINE_BINDING_NOT_FOUND";
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private static string ReadParameter(GameContracts.EffectDefinition effect, string key, string fallback)
        {
            if (effect == null || effect.parameters == null || string.IsNullOrWhiteSpace(key))
            {
                return NormalizeOrFallback(fallback, string.Empty);
            }

            for (var i = 0; i < effect.parameters.Count; i++)
            {
                var parameter = effect.parameters[i];
                if (parameter == null || !string.Equals(parameter.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return NormalizeOrFallback(parameter.value, fallback);
            }

            return NormalizeOrFallback(fallback, string.Empty);
        }

        private static bool ReadBoolParameter(GameContracts.EffectDefinition effect, string key, bool fallback)
        {
            var raw = ReadParameter(effect, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (bool.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            if (float.TryParse(
                    raw,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var numeric))
            {
                return Math.Abs(numeric) > float.Epsilon;
            }

            return fallback;
        }

        private static float ReadFloatParameter(GameContracts.EffectDefinition effect, string key, float fallback)
        {
            var raw = ReadParameter(effect, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            return float.TryParse(
                raw,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : fallback;
        }

        private static void SetInteractionEnabled(GameObject target, bool enabled)
        {
            if (target == null)
            {
                return;
            }

            var colliders = target.GetComponentsInChildren<Collider>(includeInactive: true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider != null)
                {
                    collider.enabled = enabled;
                }
            }
        }

        private static bool TrySetKnownTextComponent(GameObject target, string text, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (target == null)
            {
                reasonCode = "UI_TARGET_MISSING";
                return false;
            }

            var textMesh = target.GetComponentInChildren<TextMesh>(includeInactive: true);
            if (textMesh != null)
            {
                textMesh.text = text;
                return true;
            }

            if (TrySetReflectedText(target, "TMPro.TMP_Text, Unity.TextMeshPro", text))
            {
                return true;
            }

            if (TrySetReflectedText(target, "UnityEngine.UI.Text, UnityEngine.UI", text))
            {
                return true;
            }

            reasonCode = "UI_TEXT_COMPONENT_MISSING";
            return false;
        }

        private static bool TrySetReflectedText(GameObject target, string componentTypeName, string text)
        {
            var componentType = Type.GetType(componentTypeName);
            if (componentType == null)
            {
                return false;
            }

            var component = target.GetComponentInChildren(componentType, includeInactive: true);
            if (component == null)
            {
                return false;
            }

            var textProperty = componentType.GetProperty(
                "text",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (textProperty == null || !textProperty.CanWrite)
            {
                return false;
            }

            textProperty.SetValue(component, text, null);
            return true;
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            if (value >= 1f)
            {
                return 1f;
            }

            return value;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
