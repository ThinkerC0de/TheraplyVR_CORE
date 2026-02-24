import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/game_catalog_entry.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class GameCatalogService {
  static FirebaseFirestore? _firestoreOverride;
  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>> get _catalogCollection =>
      _firestore.collection('game_catalog');

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void clearTestingOverrides() {
    _firestoreOverride = null;
  }

  static Stream<List<GameCatalogEntry>> watchActiveCatalog() {
    return _catalogCollection.snapshots().map((snapshot) {
      final entries = snapshot.docs
          .map((doc) => GameCatalogEntry.fromMap(
                <String, dynamic>{
                  ...doc.data(),
                  if ((doc.data()['gameId'] as String? ?? '').trim().isEmpty)
                    'gameId': doc.id,
                },
              ))
          .where((entry) => entry.gameId.isNotEmpty)
          .where((entry) => entry.active)
          .toList(growable: false);

      entries.sort((left, right) {
        if (left.sortOrder == right.sortOrder) {
          return left.gameId.compareTo(right.gameId);
        }
        return left.sortOrder.compareTo(right.sortOrder);
      });
      return entries;
    });
  }
}
