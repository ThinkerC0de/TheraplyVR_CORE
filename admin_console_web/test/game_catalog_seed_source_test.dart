import 'package:admin_console_web/services/game_catalog_seed_source.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('GameCatalogSeedSource.parseCatalogSeedJson', () {
    test('sorts entries by sortOrder and gameId', () {
      const json = '''
{
  "collection": "game_catalog",
  "entries": [
    {
      "gameId": "zeta_game",
      "title": "Zeta",
      "description": "Z",
      "targetContentVersion": "1.0.0",
      "packageUri": "",
      "thumbnailUrl": "",
      "supportsSaveResume": false,
      "availableForPurchase": false,
      "requiresExplicitLicense": false,
      "runtimeLaunchEnabled": true,
      "sortOrder": 20,
      "active": true,
      "previewLines": []
    },
    {
      "gameId": "alpha_game",
      "title": "Alpha",
      "description": "A",
      "targetContentVersion": "1.0.0",
      "packageUri": "",
      "thumbnailUrl": "",
      "supportsSaveResume": false,
      "availableForPurchase": false,
      "requiresExplicitLicense": false,
      "runtimeLaunchEnabled": true,
      "sortOrder": 10,
      "active": true,
      "previewLines": []
    }
  ]
}
''';

      final parsed = GameCatalogSeedSource.parseCatalogSeedJson(json);

      expect(parsed, hasLength(2));
      expect(parsed[0].gameId, 'alpha_game');
      expect(parsed[1].gameId, 'zeta_game');
    });
  });

  group('GameCatalogSeedSource.parseExportManifestJson', () {
    test('parses valid manifest contract', () {
      const json = '''
{
  "schema": "THERAPLY_GAME_DEFINITION_EXPORT_MANIFEST",
  "schemaVersion": "2026-02-25",
  "exportedAtUtc": "2026-02-25T15:12:17.6936566Z",
  "entryCount": 2,
  "entries": [
    {
      "gameId": "demo_cube_clicker",
      "definitionAssetPath": "Assets/_YourGames/Samples/SessionFlow/DemoCubeFlowDefinition.asset",
      "definitionJsonPath": "contracts/game_definitions/demo_cube_clicker.json",
      "mobileControlSchemaJsonPath": "contracts/mobile_control_schema_demo_cube_clicker.json"
    },
    {
      "gameId": "pulse_target_tap",
      "definitionAssetPath": "Assets/_YourGames/Samples/SessionFlow/PulseTargetFlowDefinition.asset",
      "definitionJsonPath": "contracts/game_definitions/pulse_target_tap.json",
      "mobileControlSchemaJsonPath": "contracts/mobile_control_schema_pulse_target_tap.json"
    }
  ]
}
''';

      final parsed = GameCatalogSeedSource.parseExportManifestJson(json);

      expect(parsed.schema, 'THERAPLY_GAME_DEFINITION_EXPORT_MANIFEST');
      expect(parsed.entryCount, 2);
      expect(parsed.entries, hasLength(2));
      expect(parsed.entries.first.gameId, 'demo_cube_clicker');
      expect(parsed.exportedGameIds, contains('pulse_target_tap'));
    });

    test('throws when entryCount does not match entries length', () {
      const json = '''
{
  "schema": "THERAPLY_GAME_DEFINITION_EXPORT_MANIFEST",
  "schemaVersion": "2026-02-25",
  "exportedAtUtc": "2026-02-25T15:12:17.6936566Z",
  "entryCount": 3,
  "entries": [
    {
      "gameId": "demo_cube_clicker",
      "definitionAssetPath": "Assets/_YourGames/Samples/SessionFlow/DemoCubeFlowDefinition.asset",
      "definitionJsonPath": "contracts/game_definitions/demo_cube_clicker.json",
      "mobileControlSchemaJsonPath": "contracts/mobile_control_schema_demo_cube_clicker.json"
    }
  ]
}
''';

      expect(
        () => GameCatalogSeedSource.parseExportManifestJson(json),
        throwsA(isA<FormatException>()),
      );
    });
  });
}
