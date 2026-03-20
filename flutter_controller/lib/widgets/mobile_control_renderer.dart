import 'package:flutter/material.dart';
import 'package:flutter_controller/models/mobile_control_schema.dart';

class MobileControlRenderer extends StatelessWidget {
  final MobileControlSchema schema;
  final Map<String, dynamic> valuesByControlId;
  final bool locked;
  final bool lockButtons;
  final ValueChanged<MobileControlValueChange> onValueChanged;
  final ValueChanged<MobileControlDefinition> onButtonPressed;

  const MobileControlRenderer({
    super.key,
    required this.schema,
    required this.valuesByControlId,
    required this.locked,
    this.lockButtons = false,
    required this.onValueChanged,
    required this.onButtonPressed,
  });

  @override
  Widget build(BuildContext context) {
    final controls = List<MobileControlDefinition>.from(schema.controls)
      ..sort((left, right) {
        if (left.order == right.order) {
          return left.controlId.compareTo(right.controlId);
        }
        return left.order.compareTo(right.order);
      });

    final sectionsById = <String, MobileControlSectionDefinition>{
      for (final section in schema.sections) section.sectionId: section,
    };

    final sectionedControls = <String, List<MobileControlDefinition>>{};
    final unsectionedControls = <MobileControlDefinition>[];

    for (final control in controls) {
      final sectionId = control.sectionId.trim();
      if (sectionId.isEmpty) {
        unsectionedControls.add(control);
        continue;
      }

      sectionedControls.putIfAbsent(
          sectionId, () => <MobileControlDefinition>[]);
      sectionedControls[sectionId]!.add(control);
    }

    final orderedSectionIds = sectionedControls.keys.toList()
      ..sort((left, right) {
        final leftOrder = sectionsById[left]?.order ?? 0;
        final rightOrder = sectionsById[right]?.order ?? 0;
        if (leftOrder == rightOrder) {
          return left.compareTo(right);
        }
        return leftOrder.compareTo(rightOrder);
      });

    final children = <Widget>[];

    if (schema.title.trim().isNotEmpty) {
      children.add(
        Text(
          schema.title,
          style: const TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w700,
          ),
        ),
      );
    }

    if (schema.description.trim().isNotEmpty) {
      children.add(
        Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text(
            schema.description,
            style: TextStyle(
              color: Colors.grey.shade700,
              fontSize: 12,
            ),
          ),
        ),
      );
    }

    if (children.isNotEmpty) {
      children.add(const SizedBox(height: 10));
    }

    if (unsectionedControls.isNotEmpty) {
      children.addAll(
        _buildControlList(
          context,
          unsectionedControls,
        ),
      );
    }

    for (final sectionId in orderedSectionIds) {
      final sectionControls =
          sectionedControls[sectionId] ?? const <MobileControlDefinition>[];
      if (sectionControls.isEmpty) {
        continue;
      }

      final sectionLabel = sectionsById[sectionId]?.label.trim() ?? '';
      if (sectionLabel.isNotEmpty) {
        children.add(
          Padding(
            padding: const EdgeInsets.only(top: 8, bottom: 4),
            child: Text(
              sectionLabel,
              style: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        );
      }

      children.addAll(_buildControlList(context, sectionControls));
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: children,
    );
  }

  List<Widget> _buildControlList(
    BuildContext context,
    List<MobileControlDefinition> controls,
  ) {
    final widgets = <Widget>[];
    for (final control in controls) {
      widgets.add(_buildControl(context, control));
      widgets.add(const SizedBox(height: 8));
    }

    if (widgets.isNotEmpty) {
      widgets.removeLast();
    }

    return widgets;
  }

  Widget _buildControl(BuildContext context, MobileControlDefinition control) {
    final controlValue = valuesByControlId[control.controlId];
    final controlLocked = control.isButton ? lockButtons : locked;

    switch (control.type) {
      case MobileControlTypes.slider:
        return _buildSliderControl(control, controlValue, controlLocked);
      case MobileControlTypes.toggle:
        return _buildToggleControl(control, controlValue, controlLocked);
      case MobileControlTypes.select:
        return _buildSelectControl(control, controlValue, controlLocked);
      case MobileControlTypes.number:
        return _buildNumberControl(control, controlValue, controlLocked);
      case MobileControlTypes.text:
        return _buildTextControl(control, controlValue, controlLocked);
      case MobileControlTypes.button:
        return _buildButtonControl(control, controlLocked);
      default:
        return Text(
          'Unsupported control type: ${control.type}',
          style: TextStyle(
            color: Colors.red.shade700,
            fontSize: 12,
          ),
        );
    }
  }

  Widget _buildSliderControl(
    MobileControlDefinition control,
    dynamic value,
    bool disabled,
  ) {
    final min = _parseDouble(control.validation.minValue, fallback: 0.0);
    final max = _parseDouble(control.validation.maxValue, fallback: 1.0);
    final step = _parseDouble(control.validation.step, fallback: 0.1);
    final fallback = _parseDouble(control.defaultValue, fallback: min);
    final current =
        (value is num ? value.toDouble() : fallback).clamp(min, max).toDouble();

    var divisions = ((max - min) / step).round();
    if (divisions <= 0 || !divisions.isFinite) {
      divisions = 0;
    }

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey.shade100,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildControlLabel(control),
          if (control.hint.trim().isNotEmpty)
            Text(
              control.hint,
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
              ),
            ),
          Text(
            current.toStringAsFixed(_fractionDigits(step)),
            style: TextStyle(
              color: Colors.grey.shade800,
              fontWeight: FontWeight.w600,
            ),
          ),
          Slider(
            value: current,
            min: min,
            max: max,
            divisions: divisions > 0 ? divisions : null,
            onChanged: disabled
                ? null
                : (next) {
                    onValueChanged(
                      MobileControlValueChange(
                        control: control,
                        value:
                            _coerceValueType(control.binding.valueType, next),
                      ),
                    );
                  },
          ),
        ],
      ),
    );
  }

  Widget _buildToggleControl(
    MobileControlDefinition control,
    dynamic value,
    bool disabled,
  ) {
    final current = value is bool
        ? value
        : _parseBool(control.defaultValue, fallback: false);

    return SwitchListTile(
      dense: true,
      contentPadding: const EdgeInsets.symmetric(horizontal: 4),
      value: current,
      onChanged: disabled
          ? null
          : (next) {
              onValueChanged(
                MobileControlValueChange(
                  control: control,
                  value: _coerceValueType(control.binding.valueType, next),
                ),
              );
            },
      title: _buildControlLabel(control),
      subtitle: control.hint.trim().isEmpty ? null : Text(control.hint.trim()),
    );
  }

  Widget _buildSelectControl(
    MobileControlDefinition control,
    dynamic value,
    bool disabled,
  ) {
    final fallback = control.defaultValue.trim();
    final current = (value == null ? fallback : value.toString()).trim();
    final options = control.options;

    return DropdownButtonFormField<String>(
      initialValue: options.any((option) => option.value == current)
          ? current
          : (options.isNotEmpty ? options.first.value : null),
      decoration: InputDecoration(
        labelText:
            control.label.trim().isEmpty ? control.controlId : control.label,
        helperText: control.hint.trim().isEmpty ? null : control.hint,
        border: const OutlineInputBorder(),
        contentPadding:
            const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
      ),
      items: options
          .map(
            (option) => DropdownMenuItem<String>(
              value: option.value,
              child: Text(
                  option.label.trim().isEmpty ? option.value : option.label),
            ),
          )
          .toList(growable: false),
      onChanged: disabled
          ? null
          : (selected) {
              if (selected == null) {
                return;
              }
              onValueChanged(
                MobileControlValueChange(
                  control: control,
                  value: _coerceValueType(control.binding.valueType, selected),
                ),
              );
            },
    );
  }

  Widget _buildNumberControl(
    MobileControlDefinition control,
    dynamic value,
    bool disabled,
  ) {
    final fallback = control.defaultValue.trim();
    final resolved = value == null ? fallback : value.toString();

    return TextFormField(
      key: ValueKey<String>('number_${control.controlId}_$resolved'),
      initialValue: resolved,
      enabled: !disabled,
      keyboardType: const TextInputType.numberWithOptions(decimal: true),
      decoration: InputDecoration(
        labelText:
            control.label.trim().isEmpty ? control.controlId : control.label,
        helperText: control.hint.trim().isEmpty ? null : control.hint,
        border: const OutlineInputBorder(),
      ),
      onChanged: (raw) {
        onValueChanged(
          MobileControlValueChange(
            control: control,
            value: _coerceValueType(control.binding.valueType, raw),
          ),
        );
      },
    );
  }

  Widget _buildTextControl(
    MobileControlDefinition control,
    dynamic value,
    bool disabled,
  ) {
    final fallback = control.defaultValue.trim();
    final resolved = value == null ? fallback : value.toString();

    return TextFormField(
      key: ValueKey<String>('text_${control.controlId}_$resolved'),
      initialValue: resolved,
      enabled: !disabled,
      decoration: InputDecoration(
        labelText:
            control.label.trim().isEmpty ? control.controlId : control.label,
        helperText: control.hint.trim().isEmpty ? null : control.hint,
        hintText:
            control.placeholder.trim().isEmpty ? null : control.placeholder,
        border: const OutlineInputBorder(),
      ),
      onChanged: (next) {
        onValueChanged(
          MobileControlValueChange(
            control: control,
            value: _coerceValueType(control.binding.valueType, next),
          ),
        );
      },
    );
  }

  Widget _buildButtonControl(MobileControlDefinition control, bool disabled) {
    return ElevatedButton.icon(
      onPressed: disabled ? null : () => onButtonPressed(control),
      icon: const Icon(Icons.tune),
      label: Text(
        control.label.trim().isEmpty ? control.controlId : control.label,
      ),
    );
  }

  Widget _buildControlLabel(MobileControlDefinition control) {
    return Text(
      control.label.trim().isEmpty ? control.controlId : control.label,
      style: const TextStyle(
        fontWeight: FontWeight.w700,
        fontSize: 13,
      ),
    );
  }

  double _parseDouble(String value, {required double fallback}) {
    final parsed = double.tryParse(value.trim());
    if (parsed == null || parsed.isNaN || parsed.isInfinite) {
      return fallback;
    }
    return parsed;
  }

  bool _parseBool(String value, {required bool fallback}) {
    final normalized = value.trim().toLowerCase();
    if (normalized == 'true' || normalized == '1' || normalized == 'yes') {
      return true;
    }
    if (normalized == 'false' || normalized == '0' || normalized == 'no') {
      return false;
    }
    return fallback;
  }

  int _fractionDigits(double step) {
    final text = step.toString();
    final dotIndex = text.indexOf('.');
    if (dotIndex < 0) {
      return 0;
    }

    final digits = text.substring(dotIndex + 1);
    var significant = digits.length;
    while (significant > 0 && digits[significant - 1] == '0') {
      significant--;
    }
    return significant;
  }

  dynamic _coerceValueType(String valueType, dynamic raw) {
    switch (valueType) {
      case MobileControlValueTypes.integer:
        if (raw is int) {
          return raw;
        }
        if (raw is num) {
          return raw.round();
        }
        return int.tryParse(raw.toString().trim()) ?? 0;
      case MobileControlValueTypes.decimal:
        if (raw is double) {
          return raw;
        }
        if (raw is num) {
          return raw.toDouble();
        }
        return double.tryParse(raw.toString().trim()) ?? 0.0;
      case MobileControlValueTypes.boolean:
        if (raw is bool) {
          return raw;
        }
        final normalized = raw.toString().trim().toLowerCase();
        return normalized == 'true' || normalized == '1' || normalized == 'yes';
      case MobileControlValueTypes.text:
      default:
        return raw.toString();
    }
  }
}

class MobileControlValueChange {
  final MobileControlDefinition control;
  final dynamic value;

  const MobileControlValueChange({
    required this.control,
    required this.value,
  });
}
