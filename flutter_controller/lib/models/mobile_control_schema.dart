class MobileControlSchemaIds {
  static const String mobileControlSchema = 'THERAPLY_MOBILE_CONTROL_SCHEMA';
}

class MobileControlLayoutModes {
  static const String stack = 'stack';
  static const String grid = 'grid';

  static const Set<String> values = <String>{stack, grid};

  static bool isSupported(String value) => values.contains(value.trim());

  static String normalizeOrDefault(String? value) {
    final normalized = (value ?? '').trim().toLowerCase();
    return values.contains(normalized) ? normalized : stack;
  }
}

class MobileControlTypes {
  static const String slider = 'slider';
  static const String toggle = 'toggle';
  static const String select = 'select';
  static const String number = 'number';
  static const String text = 'text';
  static const String button = 'button';

  static const Set<String> values = <String>{
    slider,
    toggle,
    select,
    number,
    text,
    button,
  };

  static bool isSupported(String value) => values.contains(value.trim());

  static String normalizeOrEmpty(String? value) {
    final normalized = (value ?? '').trim().toLowerCase();
    return values.contains(normalized) ? normalized : '';
  }
}

class MobileControlPayloadTargets {
  static const String gameConfig = 'game_config';

  static const Set<String> values = <String>{gameConfig};

  static bool isSupported(String value) => values.contains(value.trim());

  static String normalizeOrDefault(String? value) {
    final normalized = (value ?? '').trim().toLowerCase();
    return values.contains(normalized) ? normalized : gameConfig;
  }
}

class MobileControlValueTypes {
  static const String integer = 'int';
  static const String decimal = 'double';
  static const String boolean = 'bool';
  static const String text = 'string';

  static const Set<String> values = <String>{
    integer,
    decimal,
    boolean,
    text,
  };

  static bool isSupported(String value) => values.contains(value.trim());

  static String normalizeOrDefault(String? value) {
    final normalized = (value ?? '').trim().toLowerCase();
    return values.contains(normalized) ? normalized : text;
  }
}

class MobileControlSchemaReasonCodes {
  static const String schemaNull = 'MOBILE_SCHEMA_NULL';
  static const String schemaIdInvalid = 'MOBILE_SCHEMA_ID_INVALID';
  static const String schemaVersionRequired = 'MOBILE_SCHEMA_VERSION_REQUIRED';
  static const String gameIdRequired = 'MOBILE_SCHEMA_GAME_ID_REQUIRED';
  static const String gameIdMismatch = 'MOBILE_SCHEMA_GAME_ID_MISMATCH';
  static const String controlsRequired = 'MOBILE_SCHEMA_CONTROLS_REQUIRED';
  static const String layoutModeUnsupported =
      'MOBILE_SCHEMA_LAYOUT_MODE_UNSUPPORTED';
  static const String sectionIdRequired = 'MOBILE_SCHEMA_SECTION_ID_REQUIRED';
  static const String sectionIdDuplicate = 'MOBILE_SCHEMA_SECTION_ID_DUPLICATE';
  static const String controlIdRequired = 'MOBILE_SCHEMA_CONTROL_ID_REQUIRED';
  static const String controlIdDuplicate = 'MOBILE_SCHEMA_CONTROL_ID_DUPLICATE';
  static const String controlTypeUnsupported =
      'MOBILE_SCHEMA_CONTROL_TYPE_UNSUPPORTED';
  static const String bindingRequired = 'MOBILE_SCHEMA_BINDING_REQUIRED';
  static const String bindingPathRequired =
      'MOBILE_SCHEMA_BINDING_PATH_REQUIRED';
  static const String bindingTargetUnsupported =
      'MOBILE_SCHEMA_BINDING_TARGET_UNSUPPORTED';
  static const String bindingValueTypeUnsupported =
      'MOBILE_SCHEMA_BINDING_VALUE_TYPE_UNSUPPORTED';
  static const String selectOptionsRequired =
      'MOBILE_SCHEMA_SELECT_OPTIONS_REQUIRED';
  static const String selectOptionValueRequired =
      'MOBILE_SCHEMA_SELECT_OPTION_VALUE_REQUIRED';
  static const String sectionReferenceMissing =
      'MOBILE_SCHEMA_SECTION_REFERENCE_MISSING';
  static const String rangeInvalid = 'MOBILE_SCHEMA_RANGE_INVALID';
  static const String visualInvalid = 'MOBILE_SCHEMA_VISUAL_INVALID';
  static const String buttonCommandRequired =
      'MOBILE_SCHEMA_BUTTON_COMMAND_REQUIRED';
}

class MobileControlSchemaParseResult {
  final MobileControlSchema? schema;
  final String reasonCode;

  const MobileControlSchemaParseResult({
    required this.schema,
    required this.reasonCode,
  });

  bool get isValid => schema != null && reasonCode.isEmpty;
}

class MobileControlSchema {
  final String schema;
  final String schemaVersion;
  final String gameId;
  final String title;
  final String description;
  final MobileControlLayoutDefinition layout;
  final MobileControlPayloadDefinition payload;
  final List<MobileControlSectionDefinition> sections;
  final List<MobileControlDefinition> controls;

  const MobileControlSchema({
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

  factory MobileControlSchema.fromMap(Map<String, dynamic> data) {
    return MobileControlSchema(
      schema: (data['schema'] as String? ?? '').trim(),
      schemaVersion: (data['schemaVersion'] as String? ?? '').trim(),
      gameId: (data['gameId'] as String? ?? '').trim(),
      title: (data['title'] as String? ?? '').trim(),
      description: (data['description'] as String? ?? '').trim(),
      layout: MobileControlLayoutDefinition.fromMap(
        _asMap(data['layout']),
      ),
      payload: MobileControlPayloadDefinition.fromMap(
        _asMap(data['payload']),
      ),
      sections: _asList(data['sections'])
          .map((entry) => MobileControlSectionDefinition.fromMap(_asMap(entry)))
          .toList(growable: false),
      controls: _asList(data['controls'])
          .map((entry) => MobileControlDefinition.fromMap(_asMap(entry)))
          .toList(growable: false),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'schema': schema,
      'schemaVersion': schemaVersion,
      'gameId': gameId,
      'title': title,
      'description': description,
      'layout': layout.toMap(),
      'payload': payload.toMap(),
      'sections':
          sections.map((entry) => entry.toMap()).toList(growable: false),
      'controls':
          controls.map((entry) => entry.toMap()).toList(growable: false),
    };
  }

  String? validate({String expectedGameId = ''}) {
    if (schema.trim().isEmpty) {
      return MobileControlSchemaReasonCodes.schemaNull;
    }

    if (schema.trim().toUpperCase() !=
        MobileControlSchemaIds.mobileControlSchema) {
      return MobileControlSchemaReasonCodes.schemaIdInvalid;
    }

    if (schemaVersion.trim().isEmpty) {
      return MobileControlSchemaReasonCodes.schemaVersionRequired;
    }

    if (gameId.trim().isEmpty) {
      return MobileControlSchemaReasonCodes.gameIdRequired;
    }

    if (expectedGameId.trim().isNotEmpty &&
        expectedGameId.trim().toLowerCase() != gameId.trim().toLowerCase()) {
      return MobileControlSchemaReasonCodes.gameIdMismatch;
    }

    if (!MobileControlLayoutModes.isSupported(layout.mode)) {
      return MobileControlSchemaReasonCodes.layoutModeUnsupported;
    }

    if (!MobileControlPayloadTargets.isSupported(payload.target)) {
      return MobileControlSchemaReasonCodes.bindingTargetUnsupported;
    }

    if (controls.isEmpty) {
      return MobileControlSchemaReasonCodes.controlsRequired;
    }

    final sectionIds = <String>{};
    for (final section in sections) {
      final sectionId = section.sectionId.trim();
      if (sectionId.isEmpty) {
        return MobileControlSchemaReasonCodes.sectionIdRequired;
      }

      final normalized = sectionId.toLowerCase();
      if (sectionIds.contains(normalized)) {
        return MobileControlSchemaReasonCodes.sectionIdDuplicate;
      }
      sectionIds.add(normalized);
    }

    final controlIds = <String>{};
    for (final control in controls) {
      final controlId = control.controlId.trim();
      if (controlId.isEmpty) {
        return MobileControlSchemaReasonCodes.controlIdRequired;
      }

      final normalizedControlId = controlId.toLowerCase();
      if (controlIds.contains(normalizedControlId)) {
        return MobileControlSchemaReasonCodes.controlIdDuplicate;
      }
      controlIds.add(normalizedControlId);

      if (!MobileControlTypes.isSupported(control.type)) {
        return MobileControlSchemaReasonCodes.controlTypeUnsupported;
      }

      if (control.sectionId.trim().isNotEmpty &&
          !sectionIds.contains(control.sectionId.trim().toLowerCase())) {
        return MobileControlSchemaReasonCodes.sectionReferenceMissing;
      }

      if (control.isButton) {
        if (control.buttonCommandId.trim().isEmpty) {
          return MobileControlSchemaReasonCodes.buttonCommandRequired;
        }
      } else {
        if (!MobileControlPayloadTargets.isSupported(control.binding.target)) {
          return MobileControlSchemaReasonCodes.bindingTargetUnsupported;
        }

        if (!MobileControlValueTypes.isSupported(control.binding.valueType)) {
          return MobileControlSchemaReasonCodes.bindingValueTypeUnsupported;
        }

        if (control.binding.path.trim().isEmpty) {
          return MobileControlSchemaReasonCodes.bindingPathRequired;
        }
      }

      if (control.isSelect && control.options.isEmpty) {
        return MobileControlSchemaReasonCodes.selectOptionsRequired;
      }

      for (final option in control.options) {
        if (option.value.trim().isEmpty) {
          return MobileControlSchemaReasonCodes.selectOptionValueRequired;
        }
      }

      if (!control.validation.hasValidRange) {
        return MobileControlSchemaReasonCodes.rangeInvalid;
      }

      if (!control.visual.isValid) {
        return MobileControlSchemaReasonCodes.visualInvalid;
      }
    }

    return null;
  }

  static MobileControlSchemaParseResult tryParse(
    dynamic raw, {
    String expectedGameId = '',
  }) {
    if (raw is! Map<String, dynamic>) {
      return const MobileControlSchemaParseResult(
        schema: null,
        reasonCode: MobileControlSchemaReasonCodes.schemaNull,
      );
    }

    final schema = MobileControlSchema.fromMap(raw);
    final reasonCode = schema.validate(expectedGameId: expectedGameId) ?? '';

    if (reasonCode.isNotEmpty) {
      return MobileControlSchemaParseResult(
        schema: null,
        reasonCode: reasonCode,
      );
    }

    return MobileControlSchemaParseResult(
      schema: schema,
      reasonCode: '',
    );
  }

  static Map<String, dynamic> _asMap(dynamic value) {
    if (value is Map<String, dynamic>) {
      return value;
    }

    if (value is Map) {
      return value.map(
        (key, item) => MapEntry(key.toString(), item),
      );
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

class MobileControlLayoutDefinition {
  final String mode;
  final int columns;

  const MobileControlLayoutDefinition({
    required this.mode,
    required this.columns,
  });

  factory MobileControlLayoutDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlLayoutDefinition(
      mode: MobileControlLayoutModes.normalizeOrDefault(
        data['mode'] as String?,
      ),
      columns: _readInt(data['columns'], 1),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'mode': mode,
      'columns': columns,
    };
  }
}

class MobileControlPayloadDefinition {
  final String target;
  final String gameConfigType;
  final int gameConfigVersion;
  final bool includeVersionInGameConfig;
  final Map<String, dynamic> staticFields;

  const MobileControlPayloadDefinition({
    required this.target,
    required this.gameConfigType,
    required this.gameConfigVersion,
    required this.includeVersionInGameConfig,
    required this.staticFields,
  });

  factory MobileControlPayloadDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlPayloadDefinition(
      target: MobileControlPayloadTargets.normalizeOrDefault(
        data['target'] as String?,
      ),
      gameConfigType: (data['gameConfigType'] as String? ?? '').trim(),
      gameConfigVersion: _readInt(data['gameConfigVersion'], 1),
      includeVersionInGameConfig:
          _readBool(data['includeVersionInGameConfig'], true),
      staticFields: MobileControlSchema._asMap(data['staticFields']),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'target': target,
      'gameConfigType': gameConfigType,
      'gameConfigVersion': gameConfigVersion,
      'includeVersionInGameConfig': includeVersionInGameConfig,
      'staticFields': staticFields,
    };
  }
}

class MobileControlSectionDefinition {
  final String sectionId;
  final String label;
  final int order;

  const MobileControlSectionDefinition({
    required this.sectionId,
    required this.label,
    required this.order,
  });

  factory MobileControlSectionDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlSectionDefinition(
      sectionId: (data['sectionId'] as String? ?? '').trim(),
      label: (data['label'] as String? ?? '').trim(),
      order: _readInt(data['order'], 0),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'sectionId': sectionId,
      'label': label,
      'order': order,
    };
  }
}

class MobileControlDefinition {
  final String controlId;
  final String type;
  final String label;
  final String hint;
  final String placeholder;
  final String sectionId;
  final int order;
  final String defaultValue;
  final String buttonCommandId;
  final MobileControlBindingDefinition binding;
  final MobileControlValidationDefinition validation;
  final MobileControlVisualDefinition visual;
  final List<MobileControlOptionDefinition> options;

  const MobileControlDefinition({
    required this.controlId,
    required this.type,
    required this.label,
    required this.hint,
    required this.placeholder,
    required this.sectionId,
    required this.order,
    required this.defaultValue,
    required this.buttonCommandId,
    required this.binding,
    required this.validation,
    required this.visual,
    required this.options,
  });

  bool get isButton => type == MobileControlTypes.button;
  bool get isSelect => type == MobileControlTypes.select;

  factory MobileControlDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlDefinition(
      controlId: (data['controlId'] as String? ?? '').trim(),
      type: MobileControlTypes.normalizeOrEmpty(data['type'] as String?),
      label: (data['label'] as String? ?? '').trim(),
      hint: (data['hint'] as String? ?? '').trim(),
      placeholder: (data['placeholder'] as String? ?? '').trim(),
      sectionId: (data['sectionId'] as String? ?? '').trim(),
      order: _readInt(data['order'], 0),
      defaultValue: (data['defaultValue'] as String? ?? '').trim(),
      buttonCommandId:
          (data['buttonCommandId'] as String? ?? 'UPDATE_CONFIG').trim(),
      binding: MobileControlBindingDefinition.fromMap(
        MobileControlSchema._asMap(data['binding']),
      ),
      validation: MobileControlValidationDefinition.fromMap(
        MobileControlSchema._asMap(data['validation']),
      ),
      visual: MobileControlVisualDefinition.fromMap(
        MobileControlSchema._asMap(data['visual']),
      ),
      options: MobileControlSchema._asList(data['options'])
          .map((entry) => MobileControlOptionDefinition.fromMap(
              MobileControlSchema._asMap(entry)))
          .toList(growable: false),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'controlId': controlId,
      'type': type,
      'label': label,
      'hint': hint,
      'placeholder': placeholder,
      'sectionId': sectionId,
      'order': order,
      'defaultValue': defaultValue,
      'buttonCommandId': buttonCommandId,
      'binding': binding.toMap(),
      'validation': validation.toMap(),
      'visual': visual.toMap(),
      'options': options.map((entry) => entry.toMap()).toList(growable: false),
    };
  }
}

class MobileControlBindingDefinition {
  final String target;
  final String path;
  final String valueType;
  final bool emitOnStartGame;
  final bool emitOnUpdateConfig;

  const MobileControlBindingDefinition({
    required this.target,
    required this.path,
    required this.valueType,
    required this.emitOnStartGame,
    required this.emitOnUpdateConfig,
  });

  factory MobileControlBindingDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlBindingDefinition(
      target: MobileControlPayloadTargets.normalizeOrDefault(
        data['target'] as String?,
      ),
      path: (data['path'] as String? ?? '').trim(),
      valueType: MobileControlValueTypes.normalizeOrDefault(
        data['valueType'] as String?,
      ),
      emitOnStartGame: _readBool(data['emitOnStartGame'], true),
      emitOnUpdateConfig: _readBool(data['emitOnUpdateConfig'], true),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'target': target,
      'path': path,
      'valueType': valueType,
      'emitOnStartGame': emitOnStartGame,
      'emitOnUpdateConfig': emitOnUpdateConfig,
    };
  }
}

class MobileControlVisualDefinition {
  final double x;
  final double y;
  final double width;
  final double height;
  final double scale;

  const MobileControlVisualDefinition({
    required this.x,
    required this.y,
    required this.width,
    required this.height,
    required this.scale,
  });

  factory MobileControlVisualDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlVisualDefinition(
      x: _readDoubleDynamic(data['x'], -1.0),
      y: _readDoubleDynamic(data['y'], -1.0),
      width: _readDoubleDynamic(data['width'], 1.0),
      height: _readDoubleDynamic(data['height'], 1.0),
      scale: _readDoubleDynamic(data['scale'], 1.0),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'x': x,
      'y': y,
      'width': width,
      'height': height,
      'scale': scale,
    };
  }

  bool get isValid {
    if (scale <= 0 || scale > 4) {
      return false;
    }
    if (width <= 0 || width > 1) {
      return false;
    }
    if (height <= 0 || height > 1) {
      return false;
    }
    if ((x >= 0 && x > 1) || (y >= 0 && y > 1)) {
      return false;
    }
    return true;
  }
}

class MobileControlValidationDefinition {
  final bool required;
  final String minValue;
  final String maxValue;
  final String step;
  final int minLength;
  final int maxLength;
  final String regex;

  const MobileControlValidationDefinition({
    required this.required,
    required this.minValue,
    required this.maxValue,
    required this.step,
    required this.minLength,
    required this.maxLength,
    required this.regex,
  });

  factory MobileControlValidationDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlValidationDefinition(
      required: _readBool(data['required'], false),
      minValue: (data['minValue'] as String? ?? '').trim(),
      maxValue: (data['maxValue'] as String? ?? '').trim(),
      step: (data['step'] as String? ?? '').trim(),
      minLength: _readInt(data['minLength'], 0),
      maxLength: _readInt(data['maxLength'], 0),
      regex: (data['regex'] as String? ?? '').trim(),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'required': required,
      'minValue': minValue,
      'maxValue': maxValue,
      'step': step,
      'minLength': minLength,
      'maxLength': maxLength,
      'regex': regex,
    };
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

class MobileControlOptionDefinition {
  final String value;
  final String label;

  const MobileControlOptionDefinition({
    required this.value,
    required this.label,
  });

  factory MobileControlOptionDefinition.fromMap(Map<String, dynamic> data) {
    return MobileControlOptionDefinition(
      value: (data['value'] as String? ?? '').trim(),
      label: (data['label'] as String? ?? '').trim(),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'value': value,
      'label': label,
    };
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

double _readDoubleDynamic(dynamic value, double fallback) {
  if (value is double) {
    return value;
  }
  if (value is num) {
    return value.toDouble();
  }
  if (value is String) {
    return double.tryParse(value.trim()) ?? fallback;
  }
  return fallback;
}
