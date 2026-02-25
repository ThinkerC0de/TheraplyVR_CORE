import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
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
  });
}
