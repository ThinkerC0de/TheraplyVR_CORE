using System;
using System.Collections.Generic;
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
            registry.Register(new DelegateEffectPlugin("spawn_prefab_wave", ExecuteSpawnPrefabWave), replaceExisting);
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
            registry.Register(new DelegateEffectPlugin("set_random_material_color", ExecuteSetRandomMaterialColor), replaceExisting);
            registry.Register(new DelegateEffectPlugin("teleport_actor", ExecuteTeleportActor), replaceExisting);
            registry.Register(new DelegateEffectPlugin("emit_hint", ExecuteEmitHint), replaceExisting);
            registry.Register(new DelegateEffectPlugin("set_locale", ExecuteSetLocale), replaceExisting);
            registry.Register(new DelegateEffectPlugin("narrator_speak", ExecuteNarratorSpeak), replaceExisting);
            registry.Register(new DelegateEffectPlugin("narrator_play_sequence", ExecuteNarratorPlaySequence), replaceExisting);
            registry.Register(new DelegateEffectPlugin("narrator_play_animation", ExecuteNarratorPlayAnimation), replaceExisting);
            registry.Register(new DelegateEffectPlugin("narrator_set_attachment", ExecuteNarratorSetAttachment), replaceExisting);
            registry.Register(new DelegateEffectPlugin("set_binding_active_by_calendar_event", ExecuteSetBindingActiveByCalendarEvent), replaceExisting);
            registry.Register(new DelegateEffectPlugin("trigger_sequence_by_calendar_event", ExecuteTriggerSequenceByCalendarEvent), replaceExisting);
            registry.Register(new DelegateEffectPlugin("load_scene", ExecuteLoadScene), replaceExisting);
            registry.Register(new DelegateEffectPlugin("unload_scene", ExecuteUnloadScene), replaceExisting);
            registry.Register(new DelegateEffectPlugin("reset_scene", ExecuteResetScene), replaceExisting);
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

        private static bool ExecuteSpawnPrefabWave(
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

            var bindingKeyPrefix = ReadParameter(effect, "bindingKeyPrefix", string.Empty);
            if (string.IsNullOrWhiteSpace(bindingKeyPrefix))
            {
                bindingKeyPrefix = ReadParameter(effect, "bindingKey", effect == null ? string.Empty : effect.binding);
            }

            var spawnPointKey = ReadParameter(effect, "spawnPointKey", string.Empty);
            var count = ReadIntParameter(effect, "count", 1);
            if (count <= 0)
            {
                reasonCode = "SPAWN_COUNT_INVALID";
                return false;
            }

            var durationSec = ReadFloatParameter(effect, "durationSec", 0f);
            if (durationSec < 0f)
            {
                reasonCode = "SPAWN_DURATION_INVALID";
                return false;
            }

            var areaSize = new Vector3(
                Mathf.Max(0f, ReadFloatParameter(effect, "areaSizeX", 0f)),
                Mathf.Max(0f, ReadFloatParameter(effect, "areaSizeY", 0f)),
                Mathf.Max(0f, ReadFloatParameter(effect, "areaSizeZ", 0f)));
            var randomYaw = ReadBoolParameter(effect, "randomYaw", true);

            return services.sceneRuntime.TrySpawnWave(
                bindingKeyPrefix,
                prefabKey,
                spawnPointKey,
                areaSize,
                count,
                durationSec,
                randomYaw,
                out reasonCode);
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

            var text = string.Empty;
            var textKey = ReadParameter(effect, "textKey", string.Empty);
            if (!string.IsNullOrWhiteSpace(textKey) &&
                services != null &&
                services.localization != null &&
                services.localization.TryResolve(textKey, out var localizedText, out _, out _))
            {
                text = NormalizeOrFallback(localizedText, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = ReadParameter(effect, "text", string.Empty);
            }

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

        private static bool ExecuteSetRandomMaterialColor(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;

            var includeInactive = ReadBoolParameter(effect, "includeInactive", true);
            var minHue = ReadFloatParameter(effect, "minHue", 0f);
            var maxHue = ReadFloatParameter(effect, "maxHue", 1f);
            var minSaturation = ReadFloatParameter(effect, "minSaturation", 0.55f);
            var maxSaturation = ReadFloatParameter(effect, "maxSaturation", 0.95f);
            var minValue = ReadFloatParameter(effect, "minValue", 0.6f);
            var maxValue = ReadFloatParameter(effect, "maxValue", 1f);
            var alpha = Clamp01(ReadFloatParameter(effect, "alpha", 1f));

            Normalize01Range(ref minHue, ref maxHue);
            Normalize01Range(ref minSaturation, ref maxSaturation);
            Normalize01Range(ref minValue, ref maxValue);

            var updatedRendererCount = 0;
            var requestedAnyTarget = false;
            var firstFailureReason = string.Empty;

            var spawnBindingPrefix = ReadParameter(effect, "spawnBindingPrefix", string.Empty);
            if (!string.IsNullOrWhiteSpace(spawnBindingPrefix))
            {
                requestedAnyTarget = true;
                if (services == null || services.sceneRuntime == null)
                {
                    firstFailureReason = "SCENE_RUNTIME_MISSING";
                }
                else if (services.sceneRuntime.TrySetRandomMaterialColorOnSpawned(
                             spawnBindingPrefix,
                             includeInactive,
                             minHue,
                             maxHue,
                             minSaturation,
                             maxSaturation,
                             minValue,
                             maxValue,
                             alpha,
                             out var spawnedCount,
                             out var spawnedReasonCode))
                {
                    updatedRendererCount += spawnedCount;
                }
                else if (string.IsNullOrWhiteSpace(firstFailureReason))
                {
                    firstFailureReason = spawnedReasonCode;
                }
            }

            var bindingKey = ReadParameter(effect, "bindingKey", effect == null ? string.Empty : effect.binding);
            if (!string.IsNullOrWhiteSpace(bindingKey))
            {
                requestedAnyTarget = true;
                if (services == null || services.bindings == null)
                {
                    if (string.IsNullOrWhiteSpace(firstFailureReason))
                    {
                        firstFailureReason = "FLOW_BINDINGS_MISSING";
                    }
                }
                else if (!services.bindings.TryGetObject(bindingKey.Trim(), out var target) || target == null)
                {
                    if (string.IsNullOrWhiteSpace(firstFailureReason))
                    {
                        firstFailureReason = "EFFECT_BINDING_NOT_FOUND";
                    }
                }
                else if (TryApplyRandomMaterialColor(
                             target,
                             includeInactive,
                             minHue,
                             maxHue,
                             minSaturation,
                             maxSaturation,
                             minValue,
                             maxValue,
                             alpha,
                             out var updatedCount))
                {
                    updatedRendererCount += updatedCount;
                }
                else if (string.IsNullOrWhiteSpace(firstFailureReason))
                {
                    firstFailureReason = "MATERIAL_COLOR_PROPERTY_NOT_FOUND";
                }
            }

            if (updatedRendererCount > 0)
            {
                reasonCode = string.Empty;
                return true;
            }

            if (!requestedAnyTarget)
            {
                reasonCode = "EFFECT_BINDING_REQUIRED";
                return false;
            }

            reasonCode = NormalizeOrFallback(firstFailureReason, "MATERIAL_COLOR_PROPERTY_NOT_FOUND");
            return false;
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

        private static bool ExecuteLoadScene(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.sceneRuntime == null)
            {
                reasonCode = "SCENE_RUNTIME_MISSING";
                return false;
            }

            var sceneName = ReadParameter(effect, "sceneName", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                return false;
            }

            var mode = ReadParameter(effect, "mode", "single");
            return services.sceneRuntime.TryLoadScene(sceneName, mode, out reasonCode);
        }

        private static bool ExecuteUnloadScene(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.sceneRuntime == null)
            {
                reasonCode = "SCENE_RUNTIME_MISSING";
                return false;
            }

            var sceneName = ReadParameter(effect, "sceneName", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                return false;
            }

            return services.sceneRuntime.TryUnloadScene(sceneName, out reasonCode);
        }

        private static bool ExecuteResetScene(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.sceneRuntime == null)
            {
                reasonCode = "SCENE_RUNTIME_MISSING";
                return false;
            }

            var sceneName = ReadParameter(effect, "sceneName", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                return false;
            }

            var mode = ReadParameter(effect, "mode", "single");
            return services.sceneRuntime.TryResetScene(sceneName, mode, out reasonCode);
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

            var hintValue = messageKey.Trim();
            if (services.localization != null &&
                services.localization.TryResolve(messageKey, out var localizedHint, out _, out _))
            {
                hintValue = NormalizeOrFallback(localizedHint, hintValue);
            }

            services.feedback.ShowHint(hintValue);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteSetLocale(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.localization == null)
            {
                reasonCode = "LOCALIZATION_SERVICE_MISSING";
                return false;
            }

            var locale = ReadParameter(effect, "locale", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(locale))
            {
                reasonCode = "LOCALIZATION_LOCALE_REQUIRED";
                return false;
            }

            return services.localization.TrySetLocale(locale.Trim(), out reasonCode);
        }

        private static bool ExecuteNarratorSpeak(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.narrator == null)
            {
                reasonCode = "NARRATOR_SERVICE_MISSING";
                return false;
            }

            if (!TryBuildNarratorLineRequest(effect, out var narratorLineRequest, out reasonCode))
            {
                return false;
            }

            return services.narrator.TrySpeak(narratorLineRequest, out reasonCode);
        }

        private static bool ExecuteNarratorPlaySequence(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.narrator == null)
            {
                reasonCode = "NARRATOR_SERVICE_MISSING";
                return false;
            }

            var lineKeysRaw = ReadParameter(effect, "lineKeys", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(lineKeysRaw))
            {
                reasonCode = "NARRATOR_SEQUENCE_KEYS_REQUIRED";
                return false;
            }

            var lineKeys = SplitLineKeys(lineKeysRaw);
            if (lineKeys.Count <= 0)
            {
                reasonCode = "NARRATOR_SEQUENCE_KEYS_REQUIRED";
                return false;
            }

            var priority = ReadIntParameter(effect, "priority", 0);
            var interruptIfBusy = ReadBoolParameter(effect, "interruptIfBusy", false);
            var simulatedDurationSec = ReadFloatParameter(effect, "simulatedDurationSec", 0f);
            var fallbackTextPrefix = ReadParameter(effect, "fallbackTextPrefix", string.Empty);

            var requests = new List<GameContracts.NarratorLineRequest>(lineKeys.Count);
            for (var i = 0; i < lineKeys.Count; i++)
            {
                var lineKey = lineKeys[i];
                if (string.IsNullOrWhiteSpace(lineKey))
                {
                    continue;
                }

                requests.Add(
                    new GameContracts.NarratorLineRequest
                    {
                        lineKey = lineKey,
                        priority = priority,
                        interruptIfBusy = i == 0 && interruptIfBusy,
                        simulatedDurationSec = simulatedDurationSec,
                        fallbackText = string.IsNullOrWhiteSpace(fallbackTextPrefix)
                            ? string.Empty
                            : fallbackTextPrefix.Trim() + " " + lineKey,
                    });
            }

            if (requests.Count <= 0)
            {
                reasonCode = "NARRATOR_SEQUENCE_KEYS_REQUIRED";
                return false;
            }

            return services.narrator.TrySpeakSequence(requests, out reasonCode);
        }

        private static bool ExecuteNarratorPlayAnimation(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.narrator == null)
            {
                reasonCode = "NARRATOR_SERVICE_MISSING";
                return false;
            }

            var actorBindingKey = ReadParameter(effect, "actorBindingKey", effect == null ? string.Empty : effect.binding);
            var trigger = ReadParameter(effect, "trigger", string.Empty);
            if (string.IsNullOrWhiteSpace(trigger))
            {
                trigger = ReadParameter(effect, "animationTrigger", string.Empty);
            }

            if (string.IsNullOrWhiteSpace(trigger))
            {
                reasonCode = "NARRATOR_ANIMATION_TRIGGER_REQUIRED";
                return false;
            }

            return services.narrator.TryPlayAnimation(actorBindingKey, trigger, out reasonCode);
        }

        private static bool ExecuteNarratorSetAttachment(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.narrator == null)
            {
                reasonCode = "NARRATOR_SERVICE_MISSING";
                return false;
            }

            var attachmentBindingKey = ReadParameter(effect, "attachmentBindingKey", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(attachmentBindingKey))
            {
                reasonCode = "NARRATOR_ATTACHMENT_BINDING_REQUIRED";
                return false;
            }

            var visible = ReadBoolParameter(effect, "visible", true);
            return services.narrator.TrySetAttachment(attachmentBindingKey, visible, out reasonCode);
        }

        private static bool ExecuteSetBindingActiveByCalendarEvent(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.calendar == null)
            {
                reasonCode = "CALENDAR_SERVICE_MISSING";
                return false;
            }

            if (services.bindings == null)
            {
                reasonCode = "FLOW_BINDINGS_MISSING";
                return false;
            }

            var eventId = ReadParameter(effect, "eventId", string.Empty);
            if (string.IsNullOrWhiteSpace(eventId))
            {
                reasonCode = "CALENDAR_EVENT_ID_REQUIRED";
                return false;
            }

            if (!services.calendar.TryEvaluateEventActive(eventId, out var isEventActive, out reasonCode))
            {
                return false;
            }

            var bindingKey = ReadParameter(effect, "bindingKey", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(bindingKey))
            {
                reasonCode = "EFFECT_BINDING_REQUIRED";
                return false;
            }

            if (!services.bindings.TryGetObject(bindingKey, out var target) || target == null)
            {
                reasonCode = "EFFECT_BINDING_NOT_FOUND";
                return false;
            }

            var activeWhenEvent = ReadBoolParameter(effect, "activeWhenEvent", true);
            target.SetActive(activeWhenEvent ? isEventActive : !isEventActive);
            reasonCode = string.Empty;
            return true;
        }

        private static bool ExecuteTriggerSequenceByCalendarEvent(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (services == null || services.calendar == null)
            {
                reasonCode = "CALENDAR_SERVICE_MISSING";
                return false;
            }

            var eventId = ReadParameter(effect, "eventId", string.Empty);
            if (string.IsNullOrWhiteSpace(eventId))
            {
                reasonCode = "CALENDAR_EVENT_ID_REQUIRED";
                return false;
            }

            if (!services.calendar.TryEvaluateEventActive(eventId, out var isEventActive, out reasonCode))
            {
                return false;
            }

            if (!isEventActive)
            {
                var skipWhenInactive = ReadBoolParameter(effect, "skipWhenInactive", true);
                if (skipWhenInactive)
                {
                    reasonCode = string.Empty;
                    return true;
                }

                reasonCode = "CALENDAR_EVENT_INACTIVE";
                return false;
            }

            var executed = false;

            var timelineKey = ReadParameter(effect, "timelineKey", string.Empty);
            if (!string.IsNullOrWhiteSpace(timelineKey))
            {
                if (services.bindings == null ||
                    !services.bindings.TryGetTimeline(timelineKey, out var timeline) ||
                    timeline == null)
                {
                    reasonCode = "TIMELINE_BINDING_NOT_FOUND";
                    return false;
                }

                timeline.Play();
                executed = true;
            }

            var lineKeysRaw = ReadParameter(effect, "lineKeys", string.Empty);
            if (!string.IsNullOrWhiteSpace(lineKeysRaw))
            {
                if (services.narrator == null)
                {
                    reasonCode = "NARRATOR_SERVICE_MISSING";
                    return false;
                }

                var lineKeys = SplitLineKeys(lineKeysRaw);
                if (lineKeys.Count <= 0)
                {
                    reasonCode = "NARRATOR_SEQUENCE_KEYS_REQUIRED";
                    return false;
                }

                var priority = ReadIntParameter(effect, "priority", 0);
                var simulatedDurationSec = ReadFloatParameter(effect, "simulatedDurationSec", 0f);
                var fallbackTextPrefix = ReadParameter(effect, "fallbackTextPrefix", string.Empty);

                var requests = new List<GameContracts.NarratorLineRequest>(lineKeys.Count);
                for (var i = 0; i < lineKeys.Count; i++)
                {
                    var lineKey = lineKeys[i];
                    if (string.IsNullOrWhiteSpace(lineKey))
                    {
                        continue;
                    }

                    requests.Add(
                        new GameContracts.NarratorLineRequest
                        {
                            lineKey = lineKey,
                            priority = priority,
                            interruptIfBusy = i == 0 && ReadBoolParameter(effect, "interruptIfBusy", false),
                            simulatedDurationSec = simulatedDurationSec,
                            fallbackText = string.IsNullOrWhiteSpace(fallbackTextPrefix)
                                ? string.Empty
                                : fallbackTextPrefix.Trim() + " " + lineKey,
                        });
                }

                if (requests.Count <= 0)
                {
                    reasonCode = "NARRATOR_SEQUENCE_KEYS_REQUIRED";
                    return false;
                }

                if (!services.narrator.TrySpeakSequence(requests, out reasonCode))
                {
                    return false;
                }

                executed = true;
            }

            if (!executed)
            {
                reasonCode = "CALENDAR_SEQUENCE_ACTION_REQUIRED";
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool TryBuildNarratorLineRequest(
            GameContracts.EffectDefinition effect,
            out GameContracts.NarratorLineRequest narratorLineRequest,
            out string reasonCode)
        {
            narratorLineRequest = null;
            reasonCode = string.Empty;

            var lineKey = ReadParameter(effect, "lineKey", effect == null ? string.Empty : effect.binding);
            if (string.IsNullOrWhiteSpace(lineKey))
            {
                reasonCode = "NARRATOR_LINE_KEY_REQUIRED";
                return false;
            }

            narratorLineRequest = new GameContracts.NarratorLineRequest
            {
                lineKey = lineKey,
                textKey = ReadParameter(effect, "textKey", string.Empty),
                audioBindingKey = ReadParameter(effect, "audioBindingKey", string.Empty),
                fallbackText = ReadParameter(effect, "fallbackText", string.Empty),
                priority = ReadIntParameter(effect, "priority", 0),
                interruptIfBusy = ReadBoolParameter(effect, "interruptIfBusy", false),
                simulatedDurationSec = ReadFloatParameter(effect, "simulatedDurationSec", 0f),
            };

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

        private static int ReadIntParameter(GameContracts.EffectDefinition effect, string key, int fallback)
        {
            var raw = ReadParameter(effect, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            return int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : fallback;
        }

        private static List<string> SplitLineKeys(string rawLineKeys)
        {
            var lineKeys = new List<string>();
            if (string.IsNullOrWhiteSpace(rawLineKeys))
            {
                return lineKeys;
            }

            var parts = rawLineKeys.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var normalized = NormalizeOrFallback(parts[i], string.Empty);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                lineKeys.Add(normalized);
            }

            return lineKeys;
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

        private static bool TryApplyRandomMaterialColor(
            GameObject target,
            bool includeInactive,
            float minHue,
            float maxHue,
            float minSaturation,
            float maxSaturation,
            float minValue,
            float maxValue,
            float alpha,
            out int updatedRendererCount)
        {
            updatedRendererCount = 0;
            if (target == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.material == null || !renderer.material.HasProperty("_Color"))
                {
                    continue;
                }

                var hue = UnityEngine.Random.Range(minHue, maxHue);
                var saturation = UnityEngine.Random.Range(minSaturation, maxSaturation);
                var value = UnityEngine.Random.Range(minValue, maxValue);
                var color = Color.HSVToRGB(hue, saturation, value);
                color.a = Clamp01(alpha);
                renderer.material.color = color;
                updatedRendererCount++;
            }

            return updatedRendererCount > 0;
        }

        private static void Normalize01Range(ref float minValue, ref float maxValue)
        {
            minValue = Clamp01(minValue);
            maxValue = Clamp01(maxValue);
            if (maxValue >= minValue)
            {
                return;
            }

            var swap = minValue;
            minValue = maxValue;
            maxValue = swap;
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
