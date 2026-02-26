import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/mobile_control_layout_contract.dart';
import 'package:flutter_controller/models/mobile_control_schema.dart';
import 'package:flutter_controller/services/game_catalog_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('GameCatalogService', () {
    late FakeFirebaseFirestore firestore;

    setUp(() {
      firestore = FakeFirebaseFirestore();
      GameCatalogService.setFirestoreInstanceForTesting(firestore);
    });

    tearDown(() {
      GameCatalogService.clearTestingOverrides();
    });

    Map<String, dynamic> buildValidLayoutContract() {
      return <String, dynamic>{
        'schema': MobileControlLayoutContractIds.mobileControlLayout,
        'schemaVersion': '2026-02-26',
        'gameId': 'demo_cube_clicker',
        'title': 'Layout',
        'layout': <String, dynamic>{
          'mode': 'stack',
          'columns': 1,
        },
        'payload': <String, dynamic>{
          'target': 'game_config',
          'gameConfigType': 'demo_cube_config_v1',
          'gameConfigVersion': 1,
          'includeVersionInGameConfig': true,
        },
        'controls': <Map<String, dynamic>>[
          <String, dynamic>{
            'controlId': 'cube_count',
            'type': 'slider',
            'label': 'Cube Count',
            'defaultValue': '12',
            'bindingKey': 'cubeCount',
            'bindingTarget': 'game_config',
            'valueType': 'int',
            'emitOnStartGame': true,
            'emitOnUpdateConfig': true,
            'minValue': '4',
            'maxValue': '40',
            'step': '1',
            'visual': <String, dynamic>{
              'x': 0.0,
              'y': 0.0,
              'width': 1.0,
              'height': 0.2,
              'scale': 1.0,
            },
            'options': const <Map<String, dynamic>>[],
          },
        ],
      };
    }

    test('watchActiveCatalog returns active entries sorted by sortOrder',
        () async {
      await firestore.collection('game_catalog').doc('game-b').set(
        <String, dynamic>{
          'gameId': 'game-b',
          'title': 'Game B',
          'sortOrder': 20,
          'active': true,
        },
      );
      await firestore.collection('game_catalog').doc('game-a').set(
        <String, dynamic>{
          'gameId': 'game-a',
          'title': 'Game A',
          'sortOrder': 10,
          'active': true,
        },
      );
      await firestore.collection('game_catalog').doc('game-x').set(
        <String, dynamic>{
          'gameId': 'game-x',
          'title': 'Game X',
          'sortOrder': 0,
          'active': false,
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.isNotEmpty);

      expect(
          entries.map((entry) => entry.gameId), <String>['game-a', 'game-b']);
    });

    test('falls back to document id when gameId field is missing', () async {
      await firestore.collection('game_catalog').doc('fallback-id').set(
        <String, dynamic>{
          'title': 'Fallback',
          'sortOrder': 1,
          'active': true,
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.isNotEmpty);

      expect(entries.single.gameId, 'fallback-id');
      expect(entries.single.title, 'Fallback');
    });

    test('parses optional mobile control schema from catalog entry', () async {
      await firestore.collection('game_catalog').doc('demo').set(
        <String, dynamic>{
          'gameId': 'demo_cube_clicker',
          'title': 'Demo',
          'active': true,
          'mobileControlSchema': <String, dynamic>{
            'schema': MobileControlSchemaIds.mobileControlSchema,
            'schemaVersion': '2026-02-25',
            'gameId': 'demo_cube_clicker',
            'controls': <Map<String, dynamic>>[
              <String, dynamic>{
                'controlId': 'cube_count',
                'type': 'slider',
                'binding': <String, dynamic>{
                  'path': 'cubeCount',
                  'valueType': 'int',
                },
              },
            ],
          },
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.isNotEmpty);

      expect(entries.single.mobileControlSchema, isNotNull);
      expect(entries.single.mobileControlSchemaReasonCode, isEmpty);
    });

    test('parses optional mobile control layout contract from catalog entry',
        () async {
      await firestore.collection('game_catalog').doc('demo').set(
        <String, dynamic>{
          'gameId': 'demo_cube_clicker',
          'title': 'Demo',
          'active': true,
          'mobileControlLayout': buildValidLayoutContract(),
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.isNotEmpty);

      expect(entries.single.mobileControlSchema, isNotNull);
      expect(
        entries.single.mobileControlSchema!.controls.single.binding.path,
        'cubeCount',
      );
      expect(entries.single.mobileControlSchemaReasonCode, isEmpty);
    });

    test(
        'falls back to legacy mobile control schema when layout contract is invalid',
        () async {
      final invalidLayout = buildValidLayoutContract();
      (invalidLayout['controls'] as List<dynamic>).first['bindingKey'] = '';

      await firestore.collection('game_catalog').doc('demo').set(
        <String, dynamic>{
          'gameId': 'demo_cube_clicker',
          'title': 'Demo',
          'active': true,
          'mobileControlLayout': invalidLayout,
          'mobileControlSchema': <String, dynamic>{
            'schema': MobileControlSchemaIds.mobileControlSchema,
            'schemaVersion': '2026-02-25',
            'gameId': 'demo_cube_clicker',
            'controls': <Map<String, dynamic>>[
              <String, dynamic>{
                'controlId': 'cube_speed',
                'type': 'slider',
                'binding': <String, dynamic>{
                  'path': 'cubeSpeed',
                  'valueType': 'double',
                },
                'validation': <String, dynamic>{
                  'minValue': '0.2',
                  'maxValue': '2.2',
                  'step': '0.1',
                },
              },
            ],
          },
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.isNotEmpty);

      expect(entries.single.mobileControlSchema, isNotNull);
      expect(
        entries.single.mobileControlSchema!.controls.single.binding.path,
        'cubeSpeed',
      );
      expect(entries.single.mobileControlSchemaReasonCode, isEmpty);
    });

    test('reports layout reason code when both layout and schema are invalid',
        () async {
      final invalidLayout = buildValidLayoutContract();
      (invalidLayout['controls'] as List<dynamic>).first['bindingKey'] = '';

      await firestore.collection('game_catalog').doc('demo').set(
        <String, dynamic>{
          'gameId': 'demo_cube_clicker',
          'title': 'Demo',
          'active': true,
          'mobileControlLayout': invalidLayout,
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.isNotEmpty);

      expect(entries.single.mobileControlSchema, isNull);
      expect(
        entries.single.mobileControlSchemaReasonCode,
        MobileControlLayoutReasonCodes.bindingKeyRequired,
      );
    });

    test(
        'parses shared catalog contract fields with backward-compatible defaults',
        () async {
      await firestore.collection('game_catalog').doc('demo').set(
        <String, dynamic>{
          'gameId': 'demo_cube_clicker',
          'title': 'Demo',
          'targetContentVersion': '1.2.0',
          'contentVersion': '1.3.0',
          'sceneKey': 'DemoCubeScene',
          'entitlementKey': 'game:demo_cube_clicker',
          'deliveryMode': 'on_demand',
          'parameterSchema': <String, dynamic>{
            'schema': 'THERAPLY_MOBILE_CONTROL_SCHEMA',
            'schemaVersion': '2026-02-25',
          },
          'active': true,
        },
      );
      await firestore.collection('game_catalog').doc('legacy').set(
        <String, dynamic>{
          'gameId': 'legacy_game',
          'title': 'Legacy',
          'targetContentVersion': '0.9.0',
          'active': true,
        },
      );

      final stream = GameCatalogService.watchActiveCatalog();
      final entries = await stream.firstWhere((value) => value.length == 2);
      final demo =
          entries.firstWhere((entry) => entry.gameId == 'demo_cube_clicker');
      final legacy =
          entries.firstWhere((entry) => entry.gameId == 'legacy_game');

      expect(demo.contentVersion, '1.3.0');
      expect(demo.sceneKey, 'DemoCubeScene');
      expect(demo.entitlementKey, 'game:demo_cube_clicker');
      expect(demo.deliveryMode, 'on_demand');
      expect(demo.parameterSchema?['schema'], 'THERAPLY_MOBILE_CONTROL_SCHEMA');

      expect(legacy.contentVersion, '0.9.0');
      expect(legacy.sceneKey, 'legacy_game');
      expect(legacy.entitlementKey, 'game:legacy_game');
      expect(legacy.deliveryMode, 'bundled');
      expect(legacy.parameterSchema, isNull);
    });
  });
}
