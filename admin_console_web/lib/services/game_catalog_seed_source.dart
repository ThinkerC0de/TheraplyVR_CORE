import 'dart:convert';

import 'package:admin_console_web/services/entitlement_admin_service.dart';
import 'package:flutter/services.dart';

class GameCatalogSeedSource {
  static const String defaultAssetPath =
      'assets/contracts/game_catalog_seed.json';
  static const String defaultManifestAssetPath =
      'assets/contracts/game_definition_export_manifest.json';

  static Future<List<AdminGameCatalogSeedEntry>> loadDefault() {
    return loadFromAsset(defaultAssetPath);
  }

  static Future<List<AdminGameCatalogSeedEntry>> loadFromAsset(
      String assetPath) async {
    final normalizedPath = assetPath.trim();
    if (normalizedPath.isEmpty) {
      throw ArgumentError('assetPath is required');
    }

    final rawJson = await rootBundle.loadString(normalizedPath);
    return parseCatalogSeedJson(rawJson);
  }

  static List<AdminGameCatalogSeedEntry> parseCatalogSeedJson(String rawJson) {
    final decoded = _parseRootObject(rawJson, context: 'Catalog seed');

    final schemaRaw = decoded['schema'];
    if (schemaRaw is String && schemaRaw.trim().isNotEmpty) {
      if (schemaRaw.trim() != 'THERAPLY_GAME_CATALOG') {
        throw FormatException('Unsupported catalog seed schema: $schemaRaw');
      }
    }

    final collection = decoded['collection'];
    if (collection is! String || collection.trim() != 'game_catalog') {
      throw const FormatException(
          'Catalog seed collection must be game_catalog.');
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

  static Future<GameDefinitionExportManifest> loadManifestDefault() {
    return loadManifestFromAsset(defaultManifestAssetPath);
  }

  static Future<GameDefinitionExportManifest> loadManifestFromAsset(
    String assetPath,
  ) async {
    final normalizedPath = assetPath.trim();
    if (normalizedPath.isEmpty) {
      throw ArgumentError('assetPath is required');
    }

    final rawJson = await rootBundle.loadString(normalizedPath);
    return parseExportManifestJson(rawJson);
  }

  static GameDefinitionExportManifest parseExportManifestJson(String rawJson) {
    final decoded = _parseRootObject(rawJson, context: 'Export manifest');
    final schema = _requireString(
      decoded,
      key: 'schema',
      context: 'Export manifest',
    );
    if (schema != 'THERAPLY_GAME_DEFINITION_EXPORT_MANIFEST') {
      throw FormatException('Unsupported export manifest schema: $schema');
    }

    final schemaVersion = _requireString(
      decoded,
      key: 'schemaVersion',
      context: 'Export manifest',
    );
    final exportedAtRaw = _requireString(
      decoded,
      key: 'exportedAtUtc',
      context: 'Export manifest',
    );
    final exportedAtUtc = DateTime.tryParse(exportedAtRaw)?.toUtc();
    if (exportedAtUtc == null) {
      throw const FormatException('Export manifest exportedAtUtc is invalid.');
    }

    final entryCountValue = decoded['entryCount'];
    final entryCount = switch (entryCountValue) {
      int value => value,
      num value => value.toInt(),
      _ =>
        throw const FormatException('Export manifest entryCount is invalid.'),
    };
    if (entryCount < 0) {
      throw const FormatException(
          'Export manifest entryCount cannot be negative.');
    }

    final entriesRaw = decoded['entries'];
    if (entriesRaw is! List) {
      throw const FormatException('Export manifest entries must be list.');
    }

    final entries = <GameDefinitionExportManifestEntry>[];
    final gameIds = <String>{};
    for (var index = 0; index < entriesRaw.length; index++) {
      final row = entriesRaw[index];
      if (row is! Map) {
        throw FormatException('Export manifest entry[$index] must be object.');
      }
      final entry = GameDefinitionExportManifestEntry.fromMap(
        Map<String, dynamic>.from(row),
        index: index,
      );

      if (!gameIds.add(entry.gameId.toLowerCase())) {
        throw FormatException(
            'Export manifest has duplicate gameId ${entry.gameId}.');
      }

      entries.add(entry);
    }

    if (entryCount != entries.length) {
      throw FormatException(
        'Export manifest entryCount mismatch. entryCount=$entryCount actual=${entries.length}.',
      );
    }

    return GameDefinitionExportManifest(
      schema: schema,
      schemaVersion: schemaVersion,
      exportedAtUtc: exportedAtUtc,
      entryCount: entryCount,
      entries: List<GameDefinitionExportManifestEntry>.unmodifiable(entries),
    );
  }

  static Map<String, dynamic> _parseRootObject(
    String rawJson, {
    required String context,
  }) {
    final decoded = jsonDecode(rawJson);
    if (decoded is! Map) {
      throw FormatException('$context root must be object.');
    }

    return Map<String, dynamic>.from(decoded);
  }

  static String _requireString(
    Map<String, dynamic> map, {
    required String key,
    required String context,
  }) {
    final raw = map[key];
    if (raw is! String || raw.trim().isEmpty) {
      throw FormatException('$context.$key is required.');
    }
    return raw.trim();
  }
}

class GameDefinitionExportManifest {
  final String schema;
  final String schemaVersion;
  final DateTime exportedAtUtc;
  final int entryCount;
  final List<GameDefinitionExportManifestEntry> entries;

  const GameDefinitionExportManifest({
    required this.schema,
    required this.schemaVersion,
    required this.exportedAtUtc,
    required this.entryCount,
    required this.entries,
  });

  Set<String> get exportedGameIds =>
      entries.map((entry) => entry.gameId).toSet();
}

class GameDefinitionExportManifestEntry {
  final String gameId;
  final String definitionAssetPath;
  final String definitionJsonPath;
  final String mobileControlSchemaJsonPath;

  const GameDefinitionExportManifestEntry({
    required this.gameId,
    required this.definitionAssetPath,
    required this.definitionJsonPath,
    required this.mobileControlSchemaJsonPath,
  });

  factory GameDefinitionExportManifestEntry.fromMap(
    Map<String, dynamic> map, {
    required int index,
  }) {
    final gameId = _readRequiredString(
      map,
      key: 'gameId',
      context: 'Export manifest entry[$index]',
    );
    final definitionJsonPath = _readRequiredString(
      map,
      key: 'definitionJsonPath',
      context: 'Export manifest entry[$index]',
    );
    final definitionAssetPath = _readRequiredString(
      map,
      key: 'definitionAssetPath',
      context: 'Export manifest entry[$index]',
    );
    final schemaPath =
        _readOptionalString(map, key: 'mobileControlSchemaJsonPath');

    return GameDefinitionExportManifestEntry(
      gameId: gameId,
      definitionAssetPath: definitionAssetPath,
      definitionJsonPath: definitionJsonPath,
      mobileControlSchemaJsonPath: schemaPath,
    );
  }

  static String _readRequiredString(
    Map<String, dynamic> map, {
    required String key,
    required String context,
  }) {
    final raw = map[key];
    if (raw is! String || raw.trim().isEmpty) {
      throw FormatException('$context.$key is required.');
    }
    return raw.trim();
  }

  static String _readOptionalString(
    Map<String, dynamic> map, {
    required String key,
  }) {
    final raw = map[key];
    if (raw is! String) {
      return '';
    }
    return raw.trim();
  }
}
