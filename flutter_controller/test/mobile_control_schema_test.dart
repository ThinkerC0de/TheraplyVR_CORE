import 'package:flutter_controller/models/mobile_control_schema.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('MobileControlSchema', () {
    Map<String, dynamic> buildValidSchema() {
      return <String, dynamic>{
        'schema': MobileControlSchemaIds.mobileControlSchema,
        'schemaVersion': '2026-02-25',
        'gameId': 'demo_cube_clicker',
        'title': 'Demo controls',
        'description': 'Authoring schema for mobile controls.',
        'layout': <String, dynamic>{
          'mode': 'stack',
          'columns': 1,
        },
        'payload': <String, dynamic>{
          'target': 'game_config',
          'gameConfigType': 'demo_cube_config',
          'gameConfigVersion': 2,
          'includeVersionInGameConfig': true,
        },
        'sections': <Map<String, dynamic>>[
          <String, dynamic>{
            'sectionId': 'setup',
            'label': 'Setup',
            'order': 0,
          },
        ],
        'controls': <Map<String, dynamic>>[
          <String, dynamic>{
            'controlId': 'cube_count',
            'type': 'slider',
            'label': 'Cube count',
            'sectionId': 'setup',
            'defaultValue': '12',
            'binding': <String, dynamic>{
              'target': 'game_config',
              'path': 'cubeCount',
              'valueType': 'int',
              'emitOnStartGame': true,
              'emitOnUpdateConfig': true,
            },
            'validation': <String, dynamic>{
              'required': true,
              'minValue': '4',
              'maxValue': '40',
              'step': '1',
            },
            'options': const <Map<String, dynamic>>[],
          },
        ],
      };
    }

    test('parses and validates a valid schema map', () {
      final result = MobileControlSchema.tryParse(
        buildValidSchema(),
        expectedGameId: 'demo_cube_clicker',
      );

      expect(result.isValid, isTrue);
      expect(result.reasonCode, isEmpty);
      expect(result.schema, isNotNull);
      expect(result.schema!.controls.single.binding.path, 'cubeCount');
      expect(result.schema!.payload.gameConfigVersion, 2);
    });

    test('rejects unsupported control type with stable reason code', () {
      final source = buildValidSchema();
      (source['controls'] as List<dynamic>).first['type'] = 'unsupported';

      final result = MobileControlSchema.tryParse(source);

      expect(result.isValid, isFalse);
      expect(
        result.reasonCode,
        MobileControlSchemaReasonCodes.controlTypeUnsupported,
      );
    });

    test('rejects gameId mismatch with stable reason code', () {
      final result = MobileControlSchema.tryParse(
        buildValidSchema(),
        expectedGameId: 'pulse_target_tap',
      );

      expect(result.isValid, isFalse);
      expect(result.reasonCode, MobileControlSchemaReasonCodes.gameIdMismatch);
    });

    test('rejects invalid numeric ranges', () {
      final source = buildValidSchema();
      final control = (source['controls'] as List<dynamic>).first;
      control['validation'] = <String, dynamic>{
        'minValue': '10',
        'maxValue': '2',
      };

      final result = MobileControlSchema.tryParse(source);

      expect(result.isValid, isFalse);
      expect(result.reasonCode, MobileControlSchemaReasonCodes.rangeInvalid);
    });
  });
}
