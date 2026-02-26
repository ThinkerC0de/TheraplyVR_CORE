import 'package:flutter_controller/models/mobile_control_layout_contract.dart';
import 'package:flutter_controller/models/mobile_control_schema.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('MobileControlLayoutContract', () {
    Map<String, dynamic> buildValidLayout() {
      return <String, dynamic>{
        'schema': MobileControlLayoutContractIds.mobileControlLayout,
        'schemaVersion': '2026-02-26',
        'gameId': 'demo_cube_clicker',
        'title': 'Demo layout',
        'description': 'Layout for dynamic controls.',
        'layout': <String, dynamic>{
          'mode': 'grid',
          'columns': 2,
        },
        'payload': <String, dynamic>{
          'target': 'game_config',
          'gameConfigType': 'demo_scene_layout',
          'gameConfigVersion': 3,
          'includeVersionInGameConfig': true,
        },
        'sections': <Map<String, dynamic>>[
          <String, dynamic>{
            'sectionId': 'main',
            'label': 'Main',
            'order': 0,
          },
        ],
        'controls': <Map<String, dynamic>>[
          <String, dynamic>{
            'controlId': 'target_count',
            'type': 'slider',
            'label': 'Target count',
            'description': 'How many targets to spawn.',
            'sectionId': 'main',
            'defaultValue': '8',
            'bindingKey': 'targetCount',
            'bindingTarget': 'game_config',
            'valueType': 'int',
            'emitOnStartGame': true,
            'emitOnUpdateConfig': true,
            'minValue': '3',
            'maxValue': '32',
            'step': '1',
            'visual': <String, dynamic>{
              'x': 0.0,
              'y': 0.0,
              'width': 1.0,
              'height': 0.3,
              'scale': 1.0,
            },
            'options': const <Map<String, dynamic>>[],
          },
        ],
      };
    }

    test('parses valid contract and converts to schema', () {
      final parseResult = MobileControlLayoutContract.tryParse(
        buildValidLayout(),
        expectedGameId: 'demo_cube_clicker',
      );

      expect(parseResult.isValid, isTrue);
      expect(parseResult.reasonCode, isEmpty);
      expect(parseResult.contract, isNotNull);

      final schema = parseResult.contract!.toMobileControlSchema();
      expect(schema.schema, MobileControlSchemaIds.mobileControlSchema);
      expect(schema.controls.single.binding.path, 'targetCount');
      expect(schema.controls.single.visual.height, 0.3);
    });

    test('rejects missing binding key for non-button control', () {
      final source = buildValidLayout();
      final control = (source['controls'] as List<dynamic>).first;
      control['bindingKey'] = '';

      final parseResult = MobileControlLayoutContract.tryParse(source);
      expect(parseResult.isValid, isFalse);
      expect(
        parseResult.reasonCode,
        MobileControlLayoutReasonCodes.bindingKeyRequired,
      );
    });

    test('rejects invalid visual metadata', () {
      final source = buildValidLayout();
      final control = (source['controls'] as List<dynamic>).first;
      control['visual'] = <String, dynamic>{
        'x': 0.0,
        'y': 0.0,
        'width': 1.4,
        'height': 0.3,
        'scale': 1.0,
      };

      final parseResult = MobileControlLayoutContract.tryParse(source);
      expect(parseResult.isValid, isFalse);
      expect(
        parseResult.reasonCode,
        MobileControlLayoutReasonCodes.visualInvalid,
      );
    });
  });
}
