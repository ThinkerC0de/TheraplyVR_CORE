import 'dart:convert';

import 'package:admin_console_web/services/entitlement_admin_service.dart';
import 'package:flutter/services.dart';

class GameCatalogSeedSource {
  static const String defaultAssetPath = 'assets/contracts/game_catalog_seed.json';

  static Future<List<AdminGameCatalogSeedEntry>> loadDefault() {
    return loadFromAsset(defaultAssetPath);
  }

  static Future<List<AdminGameCatalogSeedEntry>> loadFromAsset(String assetPath) async {
    final normalizedPath = assetPath.trim();
    if (normalizedPath.isEmpty) {
      throw ArgumentError('assetPath is required');
    }

    final rawJson = await rootBundle.loadString(normalizedPath);
    final decoded = jsonDecode(rawJson);
    if (decoded is! Map<String, dynamic>) {
      throw const FormatException('Catalog seed root must be object.');
    }

    final collection = decoded['collection'];
    if (collection is! String || collection.trim() != 'game_catalog') {
      throw const FormatException('Catalog seed collection must be game_catalog.');
    }

    final entriesRaw = decoded['entries'];
    if (entriesRaw is! List) {
      throw const FormatException('Catalog seed entries must be list.');
    }

    final entries = <AdminGameCatalogSeedEntry>[];
    for (var i = 0; i < entriesRaw.length; i++) {
      final row = entriesRaw[i];
      if (row is! Map) {
        continue;
      }

      final entry = AdminGameCatalogSeedEntry.fromSeedMap(
        Map<String, dynamic>.from(row),
      );

      if (entry.gameId.trim().isEmpty) {
        throw FormatException('Catalog entry at index $i has empty gameId.');
      }

      entries.add(entry);
    }

    if (entries.isEmpty) {
      throw const FormatException('Catalog seed contains no valid entries.');
    }

    entries.sort((a, b) {
      final byOrder = a.sortOrder.compareTo(b.sortOrder);
      if (byOrder != 0) {
        return byOrder;
      }
      return a.gameId.toLowerCase().compareTo(b.gameId.toLowerCase());
    });
    return entries;
  }
}
