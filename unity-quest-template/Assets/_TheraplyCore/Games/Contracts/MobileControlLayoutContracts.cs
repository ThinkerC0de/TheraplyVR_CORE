using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheraplyCore.Games.Contracts
{
    [CreateAssetMenu(
        fileName = "MobileControlLayout",
        menuName = "Theraply/Scene Controller/Mobile Control Layout",
        order = 430)]
    public sealed class MobileControlLayoutAsset : ScriptableObject
    {
        public MobileControlLayoutContract contract =
            MobileControlLayoutContract.CreateDefault("sample_game");

        public bool TryValidate(out string reasonCode)
        {
            return MobileControlLayoutValidator.TryValidate(contract, out reasonCode);
        }

        public bool TryBuildMobileControlSchema(
            out MobileControlSchema schema,
            out string reasonCode)
        {
            return MobileControlLayoutConverter.TryToMobileControlSchema(
                contract,
                out schema,
                out reasonCode);
        }
    }

    public static class MobileControlLayoutContractIds
    {
        public const string MobileControlLayout = "THERAPLY_MOBILE_CONTROL_LAYOUT";
    }

    public static class MobileControlLayoutReasonCodes
    {
        public const string LayoutNull = "MOBILE_LAYOUT_NULL";
        public const string SchemaIdInvalid = "MOBILE_LAYOUT_SCHEMA_ID_INVALID";
        public const string SchemaVersionRequired = "MOBILE_LAYOUT_SCHEMA_VERSION_REQUIRED";
        public const string GameIdRequired = "MOBILE_LAYOUT_GAME_ID_REQUIRED";
        public const string GameIdMismatch = "MOBILE_LAYOUT_GAME_ID_MISMATCH";
        public const string LayoutModeUnsupported = "MOBILE_LAYOUT_MODE_UNSUPPORTED";
        public const string ControlsRequired = "MOBILE_LAYOUT_CONTROLS_REQUIRED";
        public const string SectionIdRequired = "MOBILE_LAYOUT_SECTION_ID_REQUIRED";
        public const string SectionIdDuplicate = "MOBILE_LAYOUT_SECTION_ID_DUPLICATE";
        public const string SectionReferenceMissing = "MOBILE_LAYOUT_SECTION_REFERENCE_MISSING";
        public const string ControlIdRequired = "MOBILE_LAYOUT_CONTROL_ID_REQUIRED";
        public const string ControlIdDuplicate = "MOBILE_LAYOUT_CONTROL_ID_DUPLICATE";
        public const string ControlTypeUnsupported = "MOBILE_LAYOUT_CONTROL_TYPE_UNSUPPORTED";
        public const string BindingKeyRequired = "MOBILE_LAYOUT_BINDING_KEY_REQUIRED";
        public const string BindingTargetUnsupported = "MOBILE_LAYOUT_BINDING_TARGET_UNSUPPORTED";
        public const string BindingValueTypeUnsupported = "MOBILE_LAYOUT_BINDING_VALUE_TYPE_UNSUPPORTED";
        public const string ButtonCommandRequired = "MOBILE_LAYOUT_BUTTON_COMMAND_REQUIRED";
        public const string SelectOptionsRequired = "MOBILE_LAYOUT_SELECT_OPTIONS_REQUIRED";
        public const string SelectOptionValueRequired = "MOBILE_LAYOUT_SELECT_OPTION_VALUE_REQUIRED";
        public const string RangeInvalid = "MOBILE_LAYOUT_RANGE_INVALID";
        public const string VisualInvalid = "MOBILE_LAYOUT_VISUAL_INVALID";
    }

    [Serializable]
    public sealed class MobileControlLayoutContract
    {
        public string schema = MobileControlLayoutContractIds.MobileControlLayout;
        public string schemaVersion = "2026-02-26";
        public string gameId = string.Empty;
        public string title = "Mobile Controls";
        public string description = string.Empty;
        public MobileControlLayoutCanvasDefinition layout = MobileControlLayoutCanvasDefinition.CreateDefault();
        public MobileControlLayoutContractPayloadDefinition payload =
            MobileControlLayoutContractPayloadDefinition.CreateDefault();
        public List<MobileControlLayoutGroupDefinition> sections =
            new List<MobileControlLayoutGroupDefinition>();
        public List<MobileControlLayoutControlDefinition> controls =
            new List<MobileControlLayoutControlDefinition>();

        public bool TryValidate(out string reasonCode)
        {
            return MobileControlLayoutValidator.TryValidate(this, out reasonCode);
        }

        public static MobileControlLayoutContract CreateDefault(string gameId)
        {
            return new MobileControlLayoutContract
            {
                schema = MobileControlLayoutContractIds.MobileControlLayout,
                schemaVersion = "2026-02-26",
                gameId = string.IsNullOrWhiteSpace(gameId) ? string.Empty : gameId.Trim(),
                title = "Mobile Controls",
                description = string.Empty,
                layout = MobileControlLayoutCanvasDefinition.CreateDefault(),
                payload = MobileControlLayoutContractPayloadDefinition.CreateDefault(),
                sections = new List<MobileControlLayoutGroupDefinition>(),
                controls = new List<MobileControlLayoutControlDefinition>(),
            };
        }
    }

    [Serializable]
    public sealed class MobileControlLayoutCanvasDefinition
    {
        public string mode = MobileControlLayoutModes.Stack;
        public int columns = 1;

        public static MobileControlLayoutCanvasDefinition CreateDefault()
        {
            return new MobileControlLayoutCanvasDefinition
            {
                mode = MobileControlLayoutModes.Stack,
                columns = 1,
            };
        }
    }

    [Serializable]
    public sealed class MobileControlLayoutContractPayloadDefinition
    {
        public string target = MobileControlPayloadTargets.GameConfig;
        public string gameConfigType = string.Empty;
        public int gameConfigVersion = 1;
        public bool includeVersionInGameConfig = true;

        public static MobileControlLayoutContractPayloadDefinition CreateDefault()
        {
            return new MobileControlLayoutContractPayloadDefinition
            {
                target = MobileControlPayloadTargets.GameConfig,
                gameConfigType = string.Empty,
                gameConfigVersion = 1,
                includeVersionInGameConfig = true,
            };
        }
    }

    [Serializable]
    public sealed class MobileControlLayoutGroupDefinition
    {
        public string sectionId = string.Empty;
        public string label = string.Empty;
        public int order;
    }

    [Serializable]
    public sealed class MobileControlLayoutControlDefinition
    {
        public string controlId = string.Empty;
        public string type = MobileControlTypes.Text;
        public string label = string.Empty;
        public string description = string.Empty;
        public string placeholder = string.Empty;
        public string sectionId = string.Empty;
        public int order;
        public string defaultValue = string.Empty;
        public string bindingKey = string.Empty;
        public string bindingTarget = MobileControlPayloadTargets.GameConfig;
        public string valueType = MobileControlValueTypes.Text;
        public bool emitOnStartGame = true;
        public bool emitOnUpdateConfig = true;
        public string buttonCommandId = GameCommandIds.UpdateConfig;
        public bool required;
        public string minValue = string.Empty;
        public string maxValue = string.Empty;
        public string step = string.Empty;
        public int minLength;
        public int maxLength;
        public string regex = string.Empty;
        public MobileControlVisualDefinition visual = MobileControlVisualDefinition.CreateDefault();
        public List<MobileControlLayoutOptionDefinition> options =
            new List<MobileControlLayoutOptionDefinition>();
    }

    [Serializable]
    public sealed class MobileControlLayoutOptionDefinition
    {
        public string value = string.Empty;
        public string label = string.Empty;
    }

    public static class MobileControlLayoutValidator
    {
        public static bool TryValidate(MobileControlLayoutContract contract, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (contract == null)
            {
                reasonCode = MobileControlLayoutReasonCodes.LayoutNull;
                return false;
            }

            if (!string.Equals(
                    contract.schema ?? string.Empty,
                    MobileControlLayoutContractIds.MobileControlLayout,
                    StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = MobileControlLayoutReasonCodes.SchemaIdInvalid;
                return false;
            }

            if (string.IsNullOrWhiteSpace(contract.schemaVersion))
            {
                reasonCode = MobileControlLayoutReasonCodes.SchemaVersionRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(contract.gameId))
            {
                reasonCode = MobileControlLayoutReasonCodes.GameIdRequired;
                return false;
            }

            if (contract.layout != null && !MobileControlLayoutModes.IsSupported(contract.layout.mode))
            {
                reasonCode = MobileControlLayoutReasonCodes.LayoutModeUnsupported;
                return false;
            }

            if (contract.payload != null && !MobileControlPayloadTargets.IsSupported(contract.payload.target))
            {
                reasonCode = MobileControlLayoutReasonCodes.BindingTargetUnsupported;
                return false;
            }

            var sectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (contract.sections != null)
            {
                for (var sectionIndex = 0; sectionIndex < contract.sections.Count; sectionIndex++)
                {
                    var section = contract.sections[sectionIndex];
                    if (section == null || string.IsNullOrWhiteSpace(section.sectionId))
                    {
                        reasonCode = MobileControlLayoutReasonCodes.SectionIdRequired;
                        return false;
                    }

                    var normalizedSectionId = section.sectionId.Trim();
                    if (!sectionIds.Add(normalizedSectionId))
                    {
                        reasonCode = MobileControlLayoutReasonCodes.SectionIdDuplicate;
                        return false;
                    }
                }
            }

            if (contract.controls == null || contract.controls.Count == 0)
            {
                reasonCode = MobileControlLayoutReasonCodes.ControlsRequired;
                return false;
            }

            var controlIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var controlIndex = 0; controlIndex < contract.controls.Count; controlIndex++)
            {
                var control = contract.controls[controlIndex];
                if (control == null || string.IsNullOrWhiteSpace(control.controlId))
                {
                    reasonCode = MobileControlLayoutReasonCodes.ControlIdRequired;
                    return false;
                }

                var normalizedControlId = control.controlId.Trim();
                if (!controlIds.Add(normalizedControlId))
                {
                    reasonCode = MobileControlLayoutReasonCodes.ControlIdDuplicate;
                    return false;
                }

                if (!MobileControlTypes.IsSupported(control.type))
                {
                    reasonCode = MobileControlLayoutReasonCodes.ControlTypeUnsupported;
                    return false;
                }

                var hasSectionId = !string.IsNullOrWhiteSpace(control.sectionId);
                if (hasSectionId && !sectionIds.Contains(control.sectionId.Trim()))
                {
                    reasonCode = MobileControlLayoutReasonCodes.SectionReferenceMissing;
                    return false;
                }

                var normalizedType = MobileControlTypes.NormalizeOrEmpty(control.type);
                if (string.Equals(normalizedType, MobileControlTypes.Button, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(control.buttonCommandId))
                    {
                        reasonCode = MobileControlLayoutReasonCodes.ButtonCommandRequired;
                        return false;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(control.bindingKey))
                    {
                        reasonCode = MobileControlLayoutReasonCodes.BindingKeyRequired;
                        return false;
                    }

                    if (!MobileControlPayloadTargets.IsSupported(control.bindingTarget))
                    {
                        reasonCode = MobileControlLayoutReasonCodes.BindingTargetUnsupported;
                        return false;
                    }

                    if (!MobileControlValueTypes.IsSupported(control.valueType))
                    {
                        reasonCode = MobileControlLayoutReasonCodes.BindingValueTypeUnsupported;
                        return false;
                    }
                }

                if (string.Equals(normalizedType, MobileControlTypes.Select, StringComparison.Ordinal))
                {
                    if (control.options == null || control.options.Count == 0)
                    {
                        reasonCode = MobileControlLayoutReasonCodes.SelectOptionsRequired;
                        return false;
                    }

                    for (var optionIndex = 0; optionIndex < control.options.Count; optionIndex++)
                    {
                        var option = control.options[optionIndex];
                        if (option == null || string.IsNullOrWhiteSpace(option.value))
                        {
                            reasonCode = MobileControlLayoutReasonCodes.SelectOptionValueRequired;
                            return false;
                        }
                    }
                }

                if (!TryValidateRange(control, out reasonCode))
                {
                    return false;
                }

                if (!TryValidateVisual(control.visual, out reasonCode))
                {
                    return false;
                }
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool TryValidateRange(
            MobileControlLayoutControlDefinition control,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (control == null)
            {
                return true;
            }

            if (!TryReadDouble(control.minValue, out var minValue, out var hasMinValue) ||
                !TryReadDouble(control.maxValue, out var maxValue, out var hasMaxValue))
            {
                reasonCode = MobileControlLayoutReasonCodes.RangeInvalid;
                return false;
            }

            if (hasMinValue && hasMaxValue && minValue > maxValue)
            {
                reasonCode = MobileControlLayoutReasonCodes.RangeInvalid;
                return false;
            }

            if (control.minLength < 0 || control.maxLength < 0)
            {
                reasonCode = MobileControlLayoutReasonCodes.RangeInvalid;
                return false;
            }

            if (control.maxLength > 0 && control.minLength > control.maxLength)
            {
                reasonCode = MobileControlLayoutReasonCodes.RangeInvalid;
                return false;
            }

            return true;
        }

        private static bool TryValidateVisual(
            MobileControlVisualDefinition visual,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (visual == null)
            {
                return true;
            }

            if (visual.scale <= 0f || visual.scale > 4f)
            {
                reasonCode = MobileControlLayoutReasonCodes.VisualInvalid;
                return false;
            }

            if (visual.width <= 0f || visual.width > 1f)
            {
                reasonCode = MobileControlLayoutReasonCodes.VisualInvalid;
                return false;
            }

            if (visual.height <= 0f || visual.height > 1f)
            {
                reasonCode = MobileControlLayoutReasonCodes.VisualInvalid;
                return false;
            }

            if ((visual.x >= 0f && visual.x > 1f) || (visual.y >= 0f && visual.y > 1f))
            {
                reasonCode = MobileControlLayoutReasonCodes.VisualInvalid;
                return false;
            }

            return true;
        }

        private static bool TryReadDouble(string value, out double parsed, out bool hasValue)
        {
            parsed = 0d;
            hasValue = false;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            hasValue = true;
            return double.TryParse(
                value.Trim(),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out parsed);
        }
    }

    public static class MobileControlLayoutConverter
    {
        public static bool TryToMobileControlSchema(
            MobileControlLayoutContract contract,
            out MobileControlSchema schema,
            out string reasonCode)
        {
            schema = null;
            reasonCode = string.Empty;

            if (!MobileControlLayoutValidator.TryValidate(contract, out reasonCode))
            {
                return false;
            }

            var normalizedGameId = contract.gameId.Trim();
            schema = MobileControlSchema.CreateDefault(normalizedGameId);
            schema.schemaVersion = contract.schemaVersion.Trim();
            schema.title = contract.title ?? string.Empty;
            schema.description = contract.description ?? string.Empty;

            schema.layout = new MobileControlLayoutDefinition
            {
                mode = contract.layout == null
                    ? MobileControlLayoutModes.Stack
                    : MobileControlLayoutModes.NormalizeOrDefault(contract.layout.mode),
                columns = contract.layout == null ? 1 : Mathf.Max(1, contract.layout.columns),
            };

            schema.payload = new MobileControlPayloadDefinition
            {
                target = contract.payload == null
                    ? MobileControlPayloadTargets.GameConfig
                    : MobileControlPayloadTargets.NormalizeOrDefault(contract.payload.target),
                gameConfigType = contract.payload == null
                    ? string.Empty
                    : (contract.payload.gameConfigType ?? string.Empty).Trim(),
                gameConfigVersion = contract.payload == null
                    ? 1
                    : Mathf.Max(1, contract.payload.gameConfigVersion),
                includeVersionInGameConfig = contract.payload == null ||
                                             contract.payload.includeVersionInGameConfig,
            };

            schema.sections = new List<MobileControlSectionDefinition>();
            if (contract.sections != null)
            {
                for (var sectionIndex = 0; sectionIndex < contract.sections.Count; sectionIndex++)
                {
                    var section = contract.sections[sectionIndex];
                    if (section == null)
                    {
                        continue;
                    }

                    schema.sections.Add(new MobileControlSectionDefinition
                    {
                        sectionId = string.IsNullOrWhiteSpace(section.sectionId)
                            ? string.Empty
                            : section.sectionId.Trim(),
                        label = section.label ?? string.Empty,
                        order = section.order,
                    });
                }
            }

            schema.controls = new List<MobileControlDefinition>();
            if (contract.controls != null)
            {
                for (var controlIndex = 0; controlIndex < contract.controls.Count; controlIndex++)
                {
                    var control = contract.controls[controlIndex];
                    if (control == null)
                    {
                        continue;
                    }

                    var mappedControl = new MobileControlDefinition
                    {
                        controlId = string.IsNullOrWhiteSpace(control.controlId)
                            ? string.Empty
                            : control.controlId.Trim(),
                        type = MobileControlTypes.NormalizeOrEmpty(control.type),
                        label = control.label ?? string.Empty,
                        hint = control.description ?? string.Empty,
                        placeholder = control.placeholder ?? string.Empty,
                        sectionId = control.sectionId ?? string.Empty,
                        order = control.order,
                        defaultValue = control.defaultValue ?? string.Empty,
                        buttonCommandId = string.IsNullOrWhiteSpace(control.buttonCommandId)
                            ? GameCommandIds.UpdateConfig
                            : control.buttonCommandId.Trim(),
                        binding = new MobileControlBindingDefinition
                        {
                            target = MobileControlPayloadTargets.NormalizeOrDefault(control.bindingTarget),
                            path = control.bindingKey ?? string.Empty,
                            valueType = MobileControlValueTypes.NormalizeOrDefault(control.valueType),
                            emitOnStartGame = control.emitOnStartGame,
                            emitOnUpdateConfig = control.emitOnUpdateConfig,
                        },
                        validation = new MobileControlValidationDefinition
                        {
                            required = control.required,
                            minValue = control.minValue ?? string.Empty,
                            maxValue = control.maxValue ?? string.Empty,
                            step = control.step ?? string.Empty,
                            minLength = control.minLength,
                            maxLength = control.maxLength,
                            regex = control.regex ?? string.Empty,
                        },
                        visual = control.visual == null
                            ? MobileControlVisualDefinition.CreateDefault()
                            : control.visual.Clone(),
                        options = new List<MobileControlOptionDefinition>(),
                    };

                    if (control.options != null)
                    {
                        for (var optionIndex = 0; optionIndex < control.options.Count; optionIndex++)
                        {
                            var option = control.options[optionIndex];
                            if (option == null)
                            {
                                continue;
                            }

                            mappedControl.options.Add(new MobileControlOptionDefinition
                            {
                                value = option.value ?? string.Empty,
                                label = option.label ?? string.Empty,
                            });
                        }
                    }

                    schema.controls.Add(mappedControl);
                }
            }

            if (!MobileControlSchemaValidator.TryValidate(schema, out reasonCode))
            {
                schema = null;
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }
    }
}
