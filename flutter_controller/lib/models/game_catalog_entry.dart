import 'package:flutter_controller/models/mobile_control_schema.dart';

class GameCatalogDeliveryModes {
  static const String bundled = 'bundled';
  static const String onDemand = 'on_demand';

  static bool isSupported(String? value) {
    final normalized = normalize(value);
    return normalized == bundled || normalized == onDemand;
  }

  static String normalize(String? value) {
    final normalized = (value ?? '').trim().toLowerCase();
    if (normalized == onDemand) {
      return onDemand;
    }
    return bundled;
  }
}

class GameCatalogEntry {
  final String gameId;
  final String title;
  final String description;
  final String targetContentVersion;
  final String contentVersion;
  final String sceneKey;
  final String entitlementKey;
  final String deliveryMode;
  final Map<String, dynamic>? parameterSchema;
  final String packageUri;
  final String thumbnailUrl;
  final bool supportsSaveResume;
  final bool availableForPurchase;
  final bool requiresExplicitLicense;
  final bool runtimeLaunchEnabled;
  final int sortOrder;
  final bool active;
  final List<String> previewLines;
  final MobileControlSchema? mobileControlSchema;
  final String mobileControlSchemaReasonCode;

  const GameCatalogEntry({
    required this.gameId,
    required this.title,
    required this.description,
    required this.targetContentVersion,
    required this.contentVersion,
    required this.sceneKey,
    required this.entitlementKey,
    required this.deliveryMode,
    required this.parameterSchema,
    required this.packageUri,
    required this.thumbnailUrl,
    required this.supportsSaveResume,
    required this.availableForPurchase,
    required this.requiresExplicitLicense,
    required this.runtimeLaunchEnabled,
    required this.sortOrder,
    required this.active,
    required this.previewLines,
    required this.mobileControlSchema,
    required this.mobileControlSchemaReasonCode,
  });

  factory GameCatalogEntry.fromMap(Map<String, dynamic> data) {
    final rawGameId = (data['gameId'] as String? ?? '').trim();
    final targetContentVersion =
        (data['targetContentVersion'] as String? ?? '1.0.0').trim();
    final rawContentVersion =
        (data['contentVersion'] as String? ?? targetContentVersion).trim();
    final contentVersion =
        rawContentVersion.isEmpty ? targetContentVersion : rawContentVersion;
    final rawSceneKey = (data['sceneKey'] as String? ?? rawGameId).trim();
    final sceneKey = rawSceneKey.isEmpty ? rawGameId : rawSceneKey;
    final rawEntitlementKey =
        (data['entitlementKey'] as String? ?? 'game:$rawGameId').trim();
    final entitlementKey =
        rawEntitlementKey.isEmpty ? 'game:$rawGameId' : rawEntitlementKey;
    final deliveryMode = GameCatalogDeliveryModes.normalize(
      data['deliveryMode'] as String?,
    );
    final parameterSchema = _parseSchemaMap(data['parameterSchema']);
    final rawSchema = data['mobileControlSchema'];
    MobileControlSchema? parsedSchema;
    var parsedSchemaReasonCode = '';
    if (rawSchema != null) {
      final parseResult = MobileControlSchema.tryParse(
        rawSchema,
        expectedGameId: rawGameId,
      );
      if (parseResult.isValid) {
        parsedSchema = parseResult.schema;
      } else {
        parsedSchemaReasonCode = parseResult.reasonCode;
      }
    }

    return GameCatalogEntry(
      gameId: rawGameId,
      title: (data['title'] as String? ?? rawGameId).trim(),
      description: (data['description'] as String? ?? '').trim(),
      targetContentVersion: targetContentVersion,
      contentVersion: contentVersion,
      sceneKey: sceneKey,
      entitlementKey: entitlementKey,
      deliveryMode: deliveryMode,
      parameterSchema: parameterSchema,
      packageUri: (data['packageUri'] as String? ?? '').trim(),
      thumbnailUrl: (data['thumbnailUrl'] as String? ?? '').trim(),
      supportsSaveResume: data['supportsSaveResume'] as bool? ?? false,
      availableForPurchase: data['availableForPurchase'] as bool? ?? true,
      requiresExplicitLicense:
          data['requiresExplicitLicense'] as bool? ?? false,
      runtimeLaunchEnabled: data['runtimeLaunchEnabled'] as bool? ?? false,
      sortOrder: _readInt(data['sortOrder'], 0),
      active: data['active'] as bool? ?? true,
      previewLines: _parsePreviewLines(data['previewLines']),
      mobileControlSchema: parsedSchema,
      mobileControlSchemaReasonCode: parsedSchemaReasonCode,
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'gameId': gameId,
      'title': title,
      'description': description,
      'targetContentVersion': targetContentVersion,
      'contentVersion': contentVersion,
      'sceneKey': sceneKey,
      'entitlementKey': entitlementKey,
      'deliveryMode': deliveryMode,
      'parameterSchema': parameterSchema,
      'packageUri': packageUri,
      'thumbnailUrl': thumbnailUrl,
      'supportsSaveResume': supportsSaveResume,
      'availableForPurchase': availableForPurchase,
      'requiresExplicitLicense': requiresExplicitLicense,
      'runtimeLaunchEnabled': runtimeLaunchEnabled,
      'sortOrder': sortOrder,
      'active': active,
      'previewLines': List<String>.from(previewLines),
      'mobileControlSchema': mobileControlSchema?.toMap(),
      'mobileControlSchemaReasonCode': mobileControlSchemaReasonCode,
    };
  }

  static List<String> _parsePreviewLines(dynamic value) {
    if (value is! List<dynamic>) {
      return const <String>[];
    }

    final parsed = <String>[];
    for (final item in value) {
      if (item is String && item.trim().isNotEmpty) {
        parsed.add(item.trim());
      }
    }
    return parsed;
  }

  static int _readInt(dynamic value, int fallback) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      final parsed = int.tryParse(value.trim());
      if (parsed != null) {
        return parsed;
      }
    }
    return fallback;
  }

  static Map<String, dynamic>? _parseSchemaMap(dynamic value) {
    if (value is! Map) {
      return null;
    }

    return Map<String, dynamic>.from(value);
  }
}
