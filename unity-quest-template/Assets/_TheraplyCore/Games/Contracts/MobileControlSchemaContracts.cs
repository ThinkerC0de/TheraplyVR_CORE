using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Stable schema identity for mobile control metadata attached to game definitions.
    /// </summary>
    public static class MobileControlSchemaIds
    {
        public const string MobileControlSchema = "THERAPLY_MOBILE_CONTROL_SCHEMA";
    }

    /// <summary>
    /// Supported high-level layout modes for mobile control rendering.
    /// </summary>
    public static class MobileControlLayoutModes
    {
        public const string Stack = "stack";
        public const string Grid = "grid";

        private static readonly HashSet<string> SupportedModes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Stack,
                Grid,
            };

        public static bool IsSupported(string mode)
        {
            return !string.IsNullOrWhiteSpace(mode) && SupportedModes.Contains(mode.Trim());
        }

        public static string NormalizeOrDefault(string mode)
        {
            return IsSupported(mode) ? mode.Trim().ToLowerInvariant() : Stack;
        }
    }

    /// <summary>
    /// Supported control types rendered in therapist mobile setup UI.
    /// </summary>
    public static class MobileControlTypes
    {
        public const string Slider = "slider";
        public const string Toggle = "toggle";
        public const string Select = "select";
        public const string Number = "number";
        public const string Text = "text";
        public const string Button = "button";

        private static readonly HashSet<string> SupportedTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Slider,
                Toggle,
                Select,
                Number,
                Text,
                Button,
            };

        public static bool IsSupported(string type)
        {
            return !string.IsNullOrWhiteSpace(type) && SupportedTypes.Contains(type.Trim());
        }

        public static string NormalizeOrEmpty(string type)
        {
            return IsSupported(type) ? type.Trim().ToLowerInvariant() : string.Empty;
        }
    }

    /// <summary>
    /// Supported payload targets for control bindings.
    /// </summary>
    public static class MobileControlPayloadTargets
    {
        public const string GameConfig = "game_config";

        private static readonly HashSet<string> SupportedTargets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                GameConfig,
            };

        public static bool IsSupported(string target)
        {
            return !string.IsNullOrWhiteSpace(target) && SupportedTargets.Contains(target.Trim());
        }

        public static string NormalizeOrDefault(string target)
        {
            return IsSupported(target) ? target.Trim().ToLowerInvariant() : GameConfig;
        }
    }

    /// <summary>
    /// Stable value type IDs for mobile control bindings.
    /// </summary>
    public static class MobileControlValueTypes
    {
        public const string Integer = "int";
        public const string Decimal = "double";
        public const string Boolean = "bool";
        public const string Text = "string";

        private static readonly HashSet<string> SupportedTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Integer,
                Decimal,
                Boolean,
                Text,
            };

        public static bool IsSupported(string valueType)
        {
            return !string.IsNullOrWhiteSpace(valueType) && SupportedTypes.Contains(valueType.Trim());
        }

        public static string NormalizeOrDefault(string valueType)
        {
            return IsSupported(valueType) ? valueType.Trim().ToLowerInvariant() : Text;
        }
    }

    /// <summary>
    /// Canonical reason codes returned by mobile control schema validation.
    /// </summary>
    public static class MobileControlSchemaReasonCodes
    {
        public const string SchemaNull = "MOBILE_SCHEMA_NULL";
        public const string SchemaIdInvalid = "MOBILE_SCHEMA_ID_INVALID";
        public const string SchemaVersionRequired = "MOBILE_SCHEMA_VERSION_REQUIRED";
        public const string GameIdRequired = "MOBILE_SCHEMA_GAME_ID_REQUIRED";
        public const string GameIdMismatch = "MOBILE_SCHEMA_GAME_ID_MISMATCH";
        public const string ControlsRequired = "MOBILE_SCHEMA_CONTROLS_REQUIRED";
        public const string LayoutModeUnsupported = "MOBILE_SCHEMA_LAYOUT_MODE_UNSUPPORTED";
        public const string SectionIdRequired = "MOBILE_SCHEMA_SECTION_ID_REQUIRED";
        public const string SectionIdDuplicate = "MOBILE_SCHEMA_SECTION_ID_DUPLICATE";
        public const string ControlIdRequired = "MOBILE_SCHEMA_CONTROL_ID_REQUIRED";
        public const string ControlIdDuplicate = "MOBILE_SCHEMA_CONTROL_ID_DUPLICATE";
        public const string ControlTypeUnsupported = "MOBILE_SCHEMA_CONTROL_TYPE_UNSUPPORTED";
        public const string BindingRequired = "MOBILE_SCHEMA_BINDING_REQUIRED";
        public const string BindingPathRequired = "MOBILE_SCHEMA_BINDING_PATH_REQUIRED";
        public const string BindingTargetUnsupported = "MOBILE_SCHEMA_BINDING_TARGET_UNSUPPORTED";
        public const string BindingValueTypeUnsupported = "MOBILE_SCHEMA_BINDING_VALUE_TYPE_UNSUPPORTED";
        public const string SelectOptionsRequired = "MOBILE_SCHEMA_SELECT_OPTIONS_REQUIRED";
        public const string SelectOptionValueRequired = "MOBILE_SCHEMA_SELECT_OPTION_VALUE_REQUIRED";
        public const string SectionReferenceMissing = "MOBILE_SCHEMA_SECTION_REFERENCE_MISSING";
        public const string RangeInvalid = "MOBILE_SCHEMA_RANGE_INVALID";
        public const string ButtonCommandRequired = "MOBILE_SCHEMA_BUTTON_COMMAND_REQUIRED";
    }

    [Serializable]
    public sealed class MobileControlSchema
    {
        public string schema = MobileControlSchemaIds.MobileControlSchema;
        public string schemaVersion = "2026-02-25";
        public string gameId = string.Empty;
        public string title = string.Empty;
        public string description = string.Empty;
        public MobileControlLayoutDefinition layout = MobileControlLayoutDefinition.CreateDefault();
        public MobileControlPayloadDefinition payload = MobileControlPayloadDefinition.CreateDefault();
        public List<MobileControlSectionDefinition> sections = new List<MobileControlSectionDefinition>();
        public List<MobileControlDefinition> controls = new List<MobileControlDefinition>();

        public bool TryValidate(out string reasonCode)
        {
            return MobileControlSchemaValidator.TryValidate(this, out reasonCode);
        }

        public static MobileControlSchema CreateDefault(string gameId)
        {
            return new MobileControlSchema
            {
                schema = MobileControlSchemaIds.MobileControlSchema,
                schemaVersion = "2026-02-25",
                gameId = string.IsNullOrWhiteSpace(gameId) ? string.Empty : gameId.Trim(),
                title = "Mobile Controls",
                description = string.Empty,
                layout = MobileControlLayoutDefinition.CreateDefault(),
                payload = MobileControlPayloadDefinition.CreateDefault(),
                sections = new List<MobileControlSectionDefinition>(),
                controls = new List<MobileControlDefinition>(),
            };
        }
    }

    [Serializable]
    public sealed class MobileControlLayoutDefinition
    {
        public string mode = MobileControlLayoutModes.Stack;
        public int columns = 1;

        public static MobileControlLayoutDefinition CreateDefault()
        {
            return new MobileControlLayoutDefinition
            {
                mode = MobileControlLayoutModes.Stack,
                columns = 1,
            };
        }
    }

    [Serializable]
    public sealed class MobileControlPayloadDefinition
    {
        public string target = MobileControlPayloadTargets.GameConfig;
        public string gameConfigType = string.Empty;
        public int gameConfigVersion = 1;
        public bool includeVersionInGameConfig = true;

        public static MobileControlPayloadDefinition CreateDefault()
        {
            return new MobileControlPayloadDefinition
            {
                target = MobileControlPayloadTargets.GameConfig,
                gameConfigType = string.Empty,
                gameConfigVersion = 1,
                includeVersionInGameConfig = true,
            };
        }
    }

    [Serializable]
    public sealed class MobileControlSectionDefinition
    {
        public string sectionId = string.Empty;
        public string label = string.Empty;
        public int order;
    }

    [Serializable]
    public sealed class MobileControlDefinition
    {
        public string controlId = string.Empty;
        public string type = MobileControlTypes.Text;
        public string label = string.Empty;
        public string hint = string.Empty;
        public string placeholder = string.Empty;
        public string sectionId = string.Empty;
        public int order;
        public string defaultValue = string.Empty;
        public string buttonCommandId = "UPDATE_CONFIG";
        public MobileControlBindingDefinition binding = MobileControlBindingDefinition.CreateDefault();
        public MobileControlValidationDefinition validation = MobileControlValidationDefinition.CreateDefault();
        public List<MobileControlOptionDefinition> options = new List<MobileControlOptionDefinition>();
    }

    [Serializable]
    public sealed class MobileControlBindingDefinition
    {
        public string target = MobileControlPayloadTargets.GameConfig;
        public string path = string.Empty;
        public string valueType = MobileControlValueTypes.Text;
        public bool emitOnStartGame = true;
        public bool emitOnUpdateConfig = true;

        public static MobileControlBindingDefinition CreateDefault()
        {
            return new MobileControlBindingDefinition
            {
                target = MobileControlPayloadTargets.GameConfig,
                path = string.Empty,
                valueType = MobileControlValueTypes.Text,
                emitOnStartGame = true,
                emitOnUpdateConfig = true,
            };
        }
    }

    [Serializable]
    public sealed class MobileControlValidationDefinition
    {
        public bool required;
        public string minValue = string.Empty;
        public string maxValue = string.Empty;
        public string step = string.Empty;
        public int minLength;
        public int maxLength;
        public string regex = string.Empty;

        public static MobileControlValidationDefinition CreateDefault()
        {
            return new MobileControlValidationDefinition
            {
                required = false,
                minValue = string.Empty,
                maxValue = string.Empty,
                step = string.Empty,
                minLength = 0,
                maxLength = 0,
                regex = string.Empty,
            };
        }
    }

    [Serializable]
    public sealed class MobileControlOptionDefinition
    {
        public string value = string.Empty;
        public string label = string.Empty;
    }

    public static class MobileControlSchemaValidator
    {
        public static bool TryValidate(MobileControlSchema schema, out string reasonCode)
        {
            reasonCode = string.Empty;

            if (schema == null)
            {
                reasonCode = MobileControlSchemaReasonCodes.SchemaNull;
                return false;
            }

            if (!string.Equals(
                    schema.schema ?? string.Empty,
                    MobileControlSchemaIds.MobileControlSchema,
                    StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = MobileControlSchemaReasonCodes.SchemaIdInvalid;
                return false;
            }

            if (string.IsNullOrWhiteSpace(schema.schemaVersion))
            {
                reasonCode = MobileControlSchemaReasonCodes.SchemaVersionRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(schema.gameId))
            {
                reasonCode = MobileControlSchemaReasonCodes.GameIdRequired;
                return false;
            }

            if (schema.layout != null && !MobileControlLayoutModes.IsSupported(schema.layout.mode))
            {
                reasonCode = MobileControlSchemaReasonCodes.LayoutModeUnsupported;
                return false;
            }

            if (schema.payload != null && !MobileControlPayloadTargets.IsSupported(schema.payload.target))
            {
                reasonCode = MobileControlSchemaReasonCodes.BindingTargetUnsupported;
                return false;
            }

            var sectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (schema.sections != null)
            {
                for (var i = 0; i < schema.sections.Count; i++)
                {
                    var section = schema.sections[i];
                    if (section == null || string.IsNullOrWhiteSpace(section.sectionId))
                    {
                        reasonCode = MobileControlSchemaReasonCodes.SectionIdRequired;
                        return false;
                    }

                    var normalizedSectionId = section.sectionId.Trim();
                    if (!sectionIds.Add(normalizedSectionId))
                    {
                        reasonCode = MobileControlSchemaReasonCodes.SectionIdDuplicate;
                        return false;
                    }
                }
            }

            if (schema.controls == null || schema.controls.Count == 0)
            {
                reasonCode = MobileControlSchemaReasonCodes.ControlsRequired;
                return false;
            }

            var controlIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < schema.controls.Count; i++)
            {
                var control = schema.controls[i];
                if (control == null || string.IsNullOrWhiteSpace(control.controlId))
                {
                    reasonCode = MobileControlSchemaReasonCodes.ControlIdRequired;
                    return false;
                }

                var normalizedControlId = control.controlId.Trim();
                if (!controlIds.Add(normalizedControlId))
                {
                    reasonCode = MobileControlSchemaReasonCodes.ControlIdDuplicate;
                    return false;
                }

                if (!MobileControlTypes.IsSupported(control.type))
                {
                    reasonCode = MobileControlSchemaReasonCodes.ControlTypeUnsupported;
                    return false;
                }

                var normalizedType = MobileControlTypes.NormalizeOrEmpty(control.type);
                var hasSectionId = !string.IsNullOrWhiteSpace(control.sectionId);
                if (hasSectionId && !sectionIds.Contains(control.sectionId.Trim()))
                {
                    reasonCode = MobileControlSchemaReasonCodes.SectionReferenceMissing;
                    return false;
                }

                if (string.Equals(normalizedType, MobileControlTypes.Button, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(control.buttonCommandId))
                    {
                        reasonCode = MobileControlSchemaReasonCodes.ButtonCommandRequired;
                        return false;
                    }
                }
                else
                {
                    if (control.binding == null)
                    {
                        reasonCode = MobileControlSchemaReasonCodes.BindingRequired;
                        return false;
                    }

                    if (!MobileControlPayloadTargets.IsSupported(control.binding.target))
                    {
                        reasonCode = MobileControlSchemaReasonCodes.BindingTargetUnsupported;
                        return false;
                    }

                    if (!MobileControlValueTypes.IsSupported(control.binding.valueType))
                    {
                        reasonCode = MobileControlSchemaReasonCodes.BindingValueTypeUnsupported;
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(control.binding.path))
                    {
                        reasonCode = MobileControlSchemaReasonCodes.BindingPathRequired;
                        return false;
                    }
                }

                if (string.Equals(normalizedType, MobileControlTypes.Select, StringComparison.Ordinal))
                {
                    if (control.options == null || control.options.Count == 0)
                    {
                        reasonCode = MobileControlSchemaReasonCodes.SelectOptionsRequired;
                        return false;
                    }

                    for (var optionIndex = 0; optionIndex < control.options.Count; optionIndex++)
                    {
                        var option = control.options[optionIndex];
                        if (option == null || string.IsNullOrWhiteSpace(option.value))
                        {
                            reasonCode = MobileControlSchemaReasonCodes.SelectOptionValueRequired;
                            return false;
                        }
                    }
                }

                if (!TryValidateRange(control.validation, out reasonCode))
                {
                    return false;
                }
            }

            reasonCode = string.Empty;
            return true;
        }

        private static bool TryValidateRange(MobileControlValidationDefinition validation, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (validation == null)
            {
                return true;
            }

            if (!TryReadDouble(validation.minValue, out var minValue, out var hasMinValue) ||
                !TryReadDouble(validation.maxValue, out var maxValue, out var hasMaxValue))
            {
                reasonCode = MobileControlSchemaReasonCodes.RangeInvalid;
                return false;
            }

            if (hasMinValue && hasMaxValue && minValue > maxValue)
            {
                reasonCode = MobileControlSchemaReasonCodes.RangeInvalid;
                return false;
            }

            if (validation.minLength < 0 || validation.maxLength < 0)
            {
                reasonCode = MobileControlSchemaReasonCodes.RangeInvalid;
                return false;
            }

            if (validation.maxLength > 0 && validation.minLength > validation.maxLength)
            {
                reasonCode = MobileControlSchemaReasonCodes.RangeInvalid;
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
}
