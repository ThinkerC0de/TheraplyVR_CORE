import 'package:flutter_controller/models/mobile_control_schema.dart';

class MobileControlLayoutContractIds {
  static const String mobileControlLayout = 'THERAPLY_MOBILE_CONTROL_LAYOUT';
}

class MobileControlLayoutReasonCodes {
  static const String layoutNull = 'MOBILE_LAYOUT_NULL';
  static const String schemaIdInvalid = 'MOBILE_LAYOUT_SCHEMA_ID_INVALID';
  static const String schemaVersionRequired =
      'MOBILE_LAYOUT_SCHEMA_VERSION_REQUIRED';
  static const String gameIdRequired = 'MOBILE_LAYOUT_GAME_ID_REQUIRED';
  static const String gameIdMismatch = 'MOBILE_LAYOUT_GAME_ID_MISMATCH';
  static const String layoutModeUnsupported = 'MOBILE_LAYOUT_MODE_UNSUPPORTED';
  static const String controlsRequired = 'MOBILE_LAYOUT_CONTROLS_REQUIRED';
  static const String sectionIdRequired = 'MOBILE_LAYOUT_SECTION_ID_REQUIRED';
  static const String sectionIdDuplicate = 'MOBILE_LAYOUT_SECTION_ID_DUPLICATE';
  static const String sectionReferenceMissing =
      'MOBILE_LAYOUT_SECTION_REFERENCE_MISSING';
  static const String controlIdRequired = 'MOBILE_LAYOUT_CONTROL_ID_REQUIRED';
  static const String controlIdDuplicate = 'MOBILE_LAYOUT_CONTROL_ID_DUPLICATE';
  static const String controlTypeUnsupported =
      'MOBILE_LAYOUT_CONTROL_TYPE_UNSUPPORTED';
  static const String bindingKeyRequired = 'MOBILE_LAYOUT_BINDING_KEY_REQUIRED';
  static const String bindingTargetUnsupported =
      'MOBILE_LAYOUT_BINDING_TARGET_UNSUPPORTED';
  static const String bindingValueTypeUnsupported =
      'MOBILE_LAYOUT_BINDING_VALUE_TYPE_UNSUPPORTED';
  static const String buttonCommandRequired =
      'MOBILE_LAYOUT_BUTTON_COMMAND_REQUIRED';
  static const String selectOptionsRequired =
      'MOBILE_LAYOUT_SELECT_OPTIONS_REQUIRED';
  static const String selectOptionValueRequired =
      'MOBILE_LAYOUT_SELECT_OPTION_VALUE_REQUIRED';
  static const String rangeInvalid = 'MOBILE_LAYOUT_RANGE_INVALID';
  static const String visualInvalid = 'MOBILE_LAYOUT_VISUAL_INVALID';
}

class MobileControlLayoutContractParseResult {
  final MobileControlLayoutContract? contract;
  final String reasonCode;

  const MobileControlLayoutContractParseResult({
    required this.contract,
    required this.reasonCode,
  });

  bool get isValid => contract != null && reasonCode.isEmpty;
}

class MobileControlLayoutContract {
  final String schema;
  final String schemaVersion;
  final String gameId;
  final String title;
  final String description;
  final MobileControlLayoutCanvasDefinition layout;
  final MobileControlLayoutPayloadDefinition payload;
  final List<MobileControlLayoutSectionDefinition> sections;
  final List<MobileControlLayoutControlDefinition> controls;

  const MobileControlLayoutContract({
    required this.schema,
    required this.schemaVersion,
    required this.gameId,
    required this.title,
    required this.description,
    required this.layout,
    required this.payload,
    required this.sections,
    required this.controls,
  });

  factory MobileControlLayoutContract.fromMap(Map<String, dynamic> data) {
    return MobileControlLayoutContract(
      schema: (data['schema'] as String? ?? '').trim(),
      schemaVersion: (data['schemaVersion'] as String? ?? '').trim(),
      gameId: (data['gameId'] as String? ?? '').trim(),
      title: (data['title'] as String? ?? '').trim(),
      description: (data['description'] as String? ?? '').trim(),
      layout: MobileControlLayoutCanvasDefinition.fromMap(
        _asMap(data['layout']),
      ),
      payload: MobileControlLayoutPayloadDefinition.fromMap(
        _asMap(data['payload']),
      ),
      sections: _asList(data['sections'])
          .map((entry) => MobileControlLayoutSectionDefinition.fromMap(
                _asMap(entry),
              ))
          .toList(growable: false),
      controls: _asList(data['controls'])
          .map((entry) => MobileControlLayoutControlDefinition.fromMap(
                _asMap(entry),
              ))
          .toList(growable: false),
    );
  }

  String? validate({String expectedGameId = ''}) {
    if (schema.trim().isEmpty) {
      return MobileControlLayoutReasonCodes.layoutNull;
    }

    if (schema.trim().toUpperCase() !=
        MobileControlLayoutContractIds.mobileControlLayout) {
      return MobileControlLayoutReasonCodes.schemaIdInvalid;
    }

    if (schemaVersion.trim().isEmpty) {
      return MobileControlLayoutReasonCodes.schemaVersionRequired;
    }

    if (gameId.trim().isEmpty) {
      return MobileControlLayoutReasonCodes.gameIdRequired;
    }

    if (expectedGameId.trim().isNotEmpty &&
        expectedGameId.trim().toLowerCase() != gameId.trim().toLowerCase()) {
      return MobileControlLayoutReasonCodes.gameIdMismatch;
    }

    if (!MobileControlLayoutModes.isSupported(layout.mode)) {
      return MobileControlLayoutReasonCodes.layoutModeUnsupported;
    }

    if (controls.isEmpty) {
      return MobileControlLayoutReasonCodes.controlsRequired;
    }

    final sectionIds = <String>{};
    for (final section in sections) {
      final sectionId = section.sectionId.trim();
      if (sectionId.isEmpty) {
        return MobileControlLayoutReasonCodes.sectionIdRequired;
      }

      final normalized = sectionId.toLowerCase();
      if (sectionIds.contains(normalized)) {
        return MobileControlLayoutReasonCodes.sectionIdDuplicate;
      }
      sectionIds.add(normalized);
    }

    final controlIds = <String>{};
    for (final control in controls) {
      final controlId = control.controlId.trim();
      if (controlId.isEmpty) {
        return MobileControlLayoutReasonCodes.controlIdRequired;
      }

      final normalizedControlId = controlId.toLowerCase();
      if (controlIds.contains(normalizedControlId)) {
        return MobileControlLayoutReasonCodes.controlIdDuplicate;
      }
      controlIds.add(normalizedControlId);

      if (!MobileControlTypes.isSupported(control.type)) {
        return MobileControlLayoutReasonCodes.controlTypeUnsupported;
      }

      if (control.sectionId.trim().isNotEmpty &&
          !sectionIds.contains(control.sectionId.trim().toLowerCase())) {
        return MobileControlLayoutReasonCodes.sectionReferenceMissing;
      }

      if (control.isButton) {
        if (control.buttonCommandId.trim().isEmpty) {
          return MobileControlLayoutReasonCodes.buttonCommandRequired;
        }
      } else {
        if (control.bindingKey.trim().isEmpty) {
          return MobileControlLayoutReasonCodes.bindingKeyRequired;
        }
        if (!MobileControlPayloadTargets.isSupported(control.bindingTarget)) {
          return MobileControlLayoutReasonCodes.bindingTargetUnsupported;
        }
        if (!MobileControlValueTypes.isSupported(control.valueType)) {
          return MobileControlLayoutReasonCodes.bindingValueTypeUnsupported;
        }
      }

      if (control.isSelect && control.options.isEmpty) {
        return MobileControlLayoutReasonCodes.selectOptionsRequired;
      }

      for (final option in control.options) {
        if (option.value.trim().isEmpty) {
          return MobileControlLayoutReasonCodes.selectOptionValueRequired;
        }
      }

      if (!control.hasValidRange) {
        return MobileControlLayoutReasonCodes.rangeInvalid;
      }

      if (!control.visual.isValid) {
        return MobileControlLayoutReasonCodes.visualInvalid;
      }
    }

    return null;
  }

  MobileControlSchema toMobileControlSchema() {
    return MobileControlSchema(
      schema: MobileControlSchemaIds.mobileControlSchema,
      schemaVersion: schemaVersion,
      gameId: gameId,
      title: title,
      description: description,
      layout: MobileControlLayoutDefinition(
        mode: layout.mode,
        columns: layout.columns,
      ),
      payload: MobileControlPayloadDefinition(
        target: payload.target,
        gameConfigType: payload.gameConfigType,
        gameConfigVersion: payload.gameConfigVersion,
        includeVersionInGameConfig: payload.includeVersionInGameConfig,
        staticFields: const <String, dynamic>{},
      ),
      sections: sections
          .map((entry) => MobileControlSectionDefinition(
                sectionId: entry.sectionId,
                label: entry.label,
                order: entry.order,
              ))
          .toList(growable: false),
      controls: controls
          .map((entry) => MobileControlDefinition(
                controlId: entry.controlId,
                type: entry.type,
                label: entry.label,
                hint: entry.description,
                placeholder: entry.placeholder,
                sectionId: entry.sectionId,
                order: entry.order,
                defaultValue: entry.defaultValue,
                buttonCommandId: entry.buttonCommandId,
                binding: MobileControlBindingDefinition(
                  target: entry.bindingTarget,
                  path: entry.bindingKey,
                  valueType: entry.valueType,
                  emitOnStartGame: entry.emitOnStartGame,
                  emitOnUpdateConfig: entry.emitOnUpdateConfig,
                ),
                validation: MobileControlValidationDefinition(
                  required: entry.required,
                  minValue: entry.minValue,
                  maxValue: entry.maxValue,
                  step: entry.step,
                  minLength: entry.minLength,
                  maxLength: entry.maxLength,
                  regex: entry.regex,
                ),
                visual: entry.visual,
                options: entry.options
                    .map((option) => MobileControlOptionDefinition(
                          value: option.value,
                          label: option.label,
                        ))
                    .toList(growable: false),
              ))
          .toList(growable: false),
    );
  }

  static MobileControlLayoutContractParseResult tryParse(
    dynamic raw, {
    String expectedGameId = '',
  }) {
    if (raw is! Map<String, dynamic>) {
      return const MobileControlLayoutContractParseResult(
        contract: null,
        reasonCode: MobileControlLayoutReasonCodes.layoutNull,
      );
    }

    final contract = MobileControlLayoutContract.fromMap(raw);
    final reasonCode = contract.validate(expectedGameId: expectedGameId) ?? '';
    if (reasonCode.isNotEmpty) {
      return MobileControlLayoutContractParseResult(
        contract: null,
        reasonCode: reasonCode,
      );
    }

    return MobileControlLayoutContractParseResult(
      contract: contract,
      reasonCode: '',
    );
  }

  static Map<String, dynamic> _asMap(dynamic value) {
    if (value is Map<String, dynamic>) {
      return value;
    }
    if (value is Map) {
      return value.map((key, item) => MapEntry(key.toString(), item));
    }
    return <String, dynamic>{};
  }

  static List<dynamic> _asList(dynamic value) {
    if (value is List<dynamic>) {
      return value;
    }
    if (value is List) {
      return value;
    }
    return const <dynamic>[];
  }
}

class MobileControlLayoutCanvasDefinition {
  final String mode;
  final int columns;

  const MobileControlLayoutCanvasDefinition({
    required this.mode,
    required this.columns,
  });

  factory MobileControlLayoutCanvasDefinition.fromMap(
      Map<String, dynamic> data) {
    return MobileControlLayoutCanvasDefinition(
      mode:
          MobileControlLayoutModes.normalizeOrDefault(data['mode'] as String?),
      columns: _readInt(data['columns'], 1),
    );
  }
}

class MobileControlLayoutPayloadDefinition {
  final String target;
  final String gameConfigType;
  final int gameConfigVersion;
  final bool includeVersionInGameConfig;

  const MobileControlLayoutPayloadDefinition({
    required this.target,
    required this.gameConfigType,
    required this.gameConfigVersion,
    required this.includeVersionInGameConfig,
  });

  factory MobileControlLayoutPayloadDefinition.fromMap(
    Map<String, dynamic> data,
  ) {
    return MobileControlLayoutPayloadDefinition(
      target: MobileControlPayloadTargets.normalizeOrDefault(
        data['target'] as String?,
      ),
      gameConfigType: (data['gameConfigType'] as String? ?? '').trim(),
      gameConfigVersion: _readInt(data['gameConfigVersion'], 1),
      includeVersionInGameConfig:
          _readBool(data['includeVersionInGameConfig'], true),
    );
  }
}

class MobileControlLayoutSectionDefinition {
  final String sectionId;
  final String label;
  final int order;

  const MobileControlLayoutSectionDefinition({
    required this.sectionId,
    required this.label,
    required this.order,
  });

  factory MobileControlLayoutSectionDefinition.fromMap(
    Map<String, dynamic> data,
  ) {
    return MobileControlLayoutSectionDefinition(
      sectionId: (data['sectionId'] as String? ?? '').trim(),
      label: (data['label'] as String? ?? '').trim(),
      order: _readInt(data['order'], 0),
    );
  }
}

class MobileControlLayoutControlDefinition {
  final String controlId;
  final String type;
  final String label;
  final String description;
  final String placeholder;
  final String sectionId;
  final int order;
  final String defaultValue;
  final String bindingKey;
  final String bindingTarget;
  final String valueType;
  final bool emitOnStartGame;
  final bool emitOnUpdateConfig;
  final String buttonCommandId;
  final bool required;
  final String minValue;
  final String maxValue;
  final String step;
  final int minLength;
  final int maxLength;
  final String regex;
  final MobileControlVisualDefinition visual;
  final List<MobileControlLayoutOptionDefinition> options;

  const MobileControlLayoutControlDefinition({
    required this.controlId,
    required this.type,
    required this.label,
    required this.description,
    required this.placeholder,
    required this.sectionId,
    required this.order,
    required this.defaultValue,
    required this.bindingKey,
    required this.bindingTarget,
    required this.valueType,
    required this.emitOnStartGame,
    required this.emitOnUpdateConfig,
    required this.buttonCommandId,
    required this.required,
    required this.minValue,
    required this.maxValue,
    required this.step,
    required this.minLength,
    required this.maxLength,
    required this.regex,
    required this.visual,
    required this.options,
  });

  bool get isButton => type == MobileControlTypes.button;
  bool get isSelect => type == MobileControlTypes.select;

  factory MobileControlLayoutControlDefinition.fromMap(
      Map<String, dynamic> data) {
    return MobileControlLayoutControlDefinition(
      controlId: (data['controlId'] as String? ?? '').trim(),
      type: MobileControlTypes.normalizeOrEmpty(data['type'] as String?),
      label: (data['label'] as String? ?? '').trim(),
      description: (data['description'] as String? ?? '').trim(),
      placeholder: (data['placeholder'] as String? ?? '').trim(),
      sectionId: (data['sectionId'] as String? ?? '').trim(),
      order: _readInt(data['order'], 0),
      defaultValue: (data['defaultValue'] as String? ?? '').trim(),
      bindingKey: (data['bindingKey'] as String? ?? '').trim(),
      bindingTarget: MobileControlPayloadTargets.normalizeOrDefault(
        data['bindingTarget'] as String?,
      ),
      valueType: MobileControlValueTypes.normalizeOrDefault(
        data['valueType'] as String?,
      ),
      emitOnStartGame: _readBool(data['emitOnStartGame'], true),
      emitOnUpdateConfig: _readBool(data['emitOnUpdateConfig'], true),
      buttonCommandId:
          (data['buttonCommandId'] as String? ?? 'UPDATE_CONFIG').trim(),
      required: _readBool(data['required'], false),
      minValue: (data['minValue'] as String? ?? '').trim(),
      maxValue: (data['maxValue'] as String? ?? '').trim(),
      step: (data['step'] as String? ?? '').trim(),
      minLength: _readInt(data['minLength'], 0),
      maxLength: _readInt(data['maxLength'], 0),
      regex: (data['regex'] as String? ?? '').trim(),
      visual: MobileControlVisualDefinition.fromMap(
        MobileControlLayoutContract._asMap(data['visual']),
      ),
      options: MobileControlLayoutContract._asList(data['options'])
          .map((entry) => MobileControlLayoutOptionDefinition.fromMap(
                MobileControlLayoutContract._asMap(entry),
              ))
          .toList(growable: false),
    );
  }

  bool get hasValidRange {
    final min = _readDouble(minValue);
    final max = _readDouble(maxValue);
    if (minValue.trim().isNotEmpty && min == null) {
      return false;
    }
    if (maxValue.trim().isNotEmpty && max == null) {
      return false;
    }
    if (min != null && max != null && min > max) {
      return false;
    }
    if (minLength < 0 || maxLength < 0) {
      return false;
    }
    if (maxLength > 0 && minLength > maxLength) {
      return false;
    }
    return true;
  }
}

class MobileControlLayoutOptionDefinition {
  final String value;
  final String label;

  const MobileControlLayoutOptionDefinition({
    required this.value,
    required this.label,
  });

  factory MobileControlLayoutOptionDefinition.fromMap(
      Map<String, dynamic> data) {
    return MobileControlLayoutOptionDefinition(
      value: (data['value'] as String? ?? '').trim(),
      label: (data['label'] as String? ?? '').trim(),
    );
  }
}

int _readInt(dynamic value, int fallback) {
  if (value is int) {
    return value;
  }
  if (value is num) {
    return value.toInt();
  }
  if (value is String) {
    return int.tryParse(value.trim()) ?? fallback;
  }
  return fallback;
}

bool _readBool(dynamic value, bool fallback) {
  if (value is bool) {
    return value;
  }
  if (value is num) {
    return value != 0;
  }
  if (value is String) {
    final normalized = value.trim().toLowerCase();
    if (normalized == 'true' || normalized == '1' || normalized == 'yes') {
      return true;
    }
    if (normalized == 'false' || normalized == '0' || normalized == 'no') {
      return false;
    }
  }
  return fallback;
}

double? _readDouble(String value) {
  final trimmed = value.trim();
  if (trimmed.isEmpty) {
    return null;
  }
  return double.tryParse(trimmed);
}
