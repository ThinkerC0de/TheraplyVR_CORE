import 'dart:convert';

enum ContentRuntimeStatus {
  notInstalled,
  installing,
  ready,
  updateRequired,
  failed,
}

class ContentDeliveryCommandIds {
  static const String syncCatalog = 'SYNC_CATALOG';
  static const String installGame = 'INSTALL_GAME';
  static const String uninstallGame = 'UNINSTALL_GAME';
  static const String gameInstallStatus = 'GAME_INSTALL_STATUS';
}

extension ContentRuntimeStatusCodec on ContentRuntimeStatus {
  static ContentRuntimeStatus fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'INSTALLING':
        return ContentRuntimeStatus.installing;
      case 'READY':
        return ContentRuntimeStatus.ready;
      case 'UPDATE_REQUIRED':
        return ContentRuntimeStatus.updateRequired;
      case 'FAILED':
        return ContentRuntimeStatus.failed;
      case 'NOT_INSTALLED':
      default:
        return ContentRuntimeStatus.notInstalled;
    }
  }

  String get wireValue {
    switch (this) {
      case ContentRuntimeStatus.notInstalled:
        return 'NOT_INSTALLED';
      case ContentRuntimeStatus.installing:
        return 'INSTALLING';
      case ContentRuntimeStatus.ready:
        return 'READY';
      case ContentRuntimeStatus.updateRequired:
        return 'UPDATE_REQUIRED';
      case ContentRuntimeStatus.failed:
        return 'FAILED';
    }
  }
}

class PurchasedContentState {
  final String gameId;
  final bool owned;
  final String? installedVersion;
  final String targetVersion;
  final bool updateRequired;
  final bool updateOptional;
  final ContentRuntimeStatus runtimeStatus;
  final String? lastError;
  final DateTime updatedAtUtc;

  const PurchasedContentState({
    required this.gameId,
    required this.owned,
    required this.installedVersion,
    required this.targetVersion,
    required this.updateRequired,
    required this.updateOptional,
    required this.runtimeStatus,
    required this.lastError,
    required this.updatedAtUtc,
  });

  bool get isInstalled =>
      installedVersion != null && installedVersion!.isNotEmpty;

  bool get isLaunchable => owned && runtimeStatus == ContentRuntimeStatus.ready;

  factory PurchasedContentState.fromJson(Map<String, dynamic> json) {
    return PurchasedContentState(
      gameId: json['gameId'] as String? ?? '',
      owned: json['owned'] as bool? ?? false,
      installedVersion: json['installedVersion'] as String?,
      targetVersion: json['targetVersion'] as String? ?? '',
      updateRequired: json['updateRequired'] as bool? ?? false,
      updateOptional: json['updateOptional'] as bool? ?? false,
      runtimeStatus: ContentRuntimeStatusCodec.fromWire(
        json['runtimeStatus'] as String?,
      ),
      lastError: json['lastError'] as String?,
      updatedAtUtc:
          DateTime.tryParse(json['updatedAtUtc'] as String? ?? '')?.toUtc() ??
              DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'gameId': gameId,
      'owned': owned,
      'installedVersion': installedVersion,
      'targetVersion': targetVersion,
      'updateRequired': updateRequired,
      'updateOptional': updateOptional,
      'runtimeStatus': runtimeStatus.wireValue,
      'lastError': lastError,
      'updatedAtUtc': updatedAtUtc.toUtc().toIso8601String(),
    };
  }

  PurchasedContentState copyWith({
    String? gameId,
    bool? owned,
    Object? installedVersion = _sentinel,
    String? targetVersion,
    bool? updateRequired,
    bool? updateOptional,
    ContentRuntimeStatus? runtimeStatus,
    Object? lastError = _sentinel,
    DateTime? updatedAtUtc,
  }) {
    return PurchasedContentState(
      gameId: gameId ?? this.gameId,
      owned: owned ?? this.owned,
      installedVersion: identical(installedVersion, _sentinel)
          ? this.installedVersion
          : installedVersion as String?,
      targetVersion: targetVersion ?? this.targetVersion,
      updateRequired: updateRequired ?? this.updateRequired,
      updateOptional: updateOptional ?? this.updateOptional,
      runtimeStatus: runtimeStatus ?? this.runtimeStatus,
      lastError: identical(lastError, _sentinel)
          ? this.lastError
          : lastError as String?,
      updatedAtUtc: updatedAtUtc ?? this.updatedAtUtc,
    );
  }
}

enum ContentDeliveryAction {
  requestInstallOrUpdate,
  markInstallSuccess,
  markFailure,
}

class ContentDeliveryTransitionRule {
  static ContentRuntimeStatus nextStatus({
    required ContentRuntimeStatus current,
    required ContentDeliveryAction action,
  }) {
    switch (action) {
      case ContentDeliveryAction.requestInstallOrUpdate:
        if (current == ContentRuntimeStatus.ready) {
          return ContentRuntimeStatus.ready;
        }
        return ContentRuntimeStatus.installing;
      case ContentDeliveryAction.markInstallSuccess:
        return ContentRuntimeStatus.ready;
      case ContentDeliveryAction.markFailure:
        return ContentRuntimeStatus.failed;
    }
  }
}

class ContentInstallStatusSignal {
  final PurchasedContentState state;

  const ContentInstallStatusSignal({
    required this.state,
  });

  static ContentInstallStatusSignal? tryFromNetworkMessage(
    Map<String, dynamic> message,
  ) {
    final commandId = message['commandId'] as String?;
    if (commandId != ContentDeliveryCommandIds.gameInstallStatus) {
      return null;
    }

    final payload = _decodePayloadMap(message['payload']);
    if (payload == null) {
      return null;
    }

    return ContentInstallStatusSignal(
      state: PurchasedContentState.fromJson(payload),
    );
  }

  static Map<String, dynamic>? _decodePayloadMap(dynamic payload) {
    if (payload is! String || payload.isEmpty) {
      return null;
    }

    try {
      final payloadString = utf8.decode(base64Decode(payload));
      final decoded = jsonDecode(payloadString);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
    } catch (_) {
      return null;
    }

    return null;
  }
}

class ContentDeliveryRequests {
  static Map<String, dynamic> buildSyncCatalogRequest({
    required String actorId,
    String? role,
    DateTime? issuedAtUtc,
  }) {
    return <String, dynamic>{
      'actorId': actorId,
      'role': role ?? 'THERAPIST',
      'issuedAtUtc':
          (issuedAtUtc ?? DateTime.now().toUtc()).toUtc().toIso8601String(),
    };
  }

  static Map<String, dynamic> buildInstallRequest({
    required String actorId,
    required String gameId,
    required String targetVersion,
    String? packageUri,
    DateTime? issuedAtUtc,
  }) {
    final payload = <String, dynamic>{
      'actorId': actorId,
      'gameId': gameId,
      'targetVersion': targetVersion,
      'issuedAtUtc':
          (issuedAtUtc ?? DateTime.now().toUtc()).toUtc().toIso8601String(),
    };
    if (packageUri != null && packageUri.trim().isNotEmpty) {
      payload['packageUri'] = packageUri.trim();
    }
    return payload;
  }

  static Map<String, dynamic> buildUninstallRequest({
    required String actorId,
    required String gameId,
    DateTime? issuedAtUtc,
  }) {
    return <String, dynamic>{
      'actorId': actorId,
      'gameId': gameId,
      'issuedAtUtc':
          (issuedAtUtc ?? DateTime.now().toUtc()).toUtc().toIso8601String(),
    };
  }
}

const Object _sentinel = Object();
