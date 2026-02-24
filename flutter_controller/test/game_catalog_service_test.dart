import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
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
  });
}
