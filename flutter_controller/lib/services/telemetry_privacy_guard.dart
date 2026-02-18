import 'package:flutter_controller/models/pseudonymization_contract.dart';

class TelemetryPrivacyGuard {
  static const Set<String> _forbiddenKeys = <String>{
    'firstname',
    'lastName',
    'lastname',
    'email',
    'mail',
    'phone',
    'address',
    'studentid',
    'therapistid',
    'fullname',
    'full_name',
  };

  static final RegExp _emailPattern =
      RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$');

  static Map<String, dynamic> validateForDispatch(
    SessionTelemetryPayload payload,
  ) {
    final telemetryMap = payload.toJson();
    _assertMapHasNoForbiddenKeysOrValues(telemetryMap, path: 'payload');

    if (payload.containsDirectIdentifiers) {
      throw const TelemetryPrivacyViolationException(
        code: 'DIRECT_IDENTIFIER_DETECTED',
        message: 'Telemetry payload contains direct identifier fields.',
      );
    }

    return telemetryMap;
  }

  static void _assertMapHasNoForbiddenKeysOrValues(
    Map<String, dynamic> data, {
    required String path,
  }) {
    for (final entry in data.entries) {
      final keyLower = entry.key.toLowerCase();
      if (_forbiddenKeys.contains(keyLower)) {
        throw TelemetryPrivacyViolationException(
          code: 'FORBIDDEN_KEY',
          message: 'Forbidden key detected at $path.${entry.key}',
        );
      }

      final childPath = '$path.${entry.key}';
      final value = entry.value;
      if (value is Map<String, dynamic>) {
        _assertMapHasNoForbiddenKeysOrValues(value, path: childPath);
        continue;
      }

      if (value is List) {
        _assertListHasNoForbiddenKeysOrValues(value, path: childPath);
        continue;
      }

      if (value is String && _emailPattern.hasMatch(value.trim())) {
        throw TelemetryPrivacyViolationException(
          code: 'FORBIDDEN_VALUE_EMAIL',
          message: 'Potential email detected at $childPath',
        );
      }
    }
  }

  static void _assertListHasNoForbiddenKeysOrValues(
    List<dynamic> data, {
    required String path,
  }) {
    for (var i = 0; i < data.length; i += 1) {
      final value = data[i];
      final childPath = '$path[$i]';
      if (value is Map<String, dynamic>) {
        _assertMapHasNoForbiddenKeysOrValues(value, path: childPath);
        continue;
      }

      if (value is List) {
        _assertListHasNoForbiddenKeysOrValues(value, path: childPath);
        continue;
      }

      if (value is String && _emailPattern.hasMatch(value.trim())) {
        throw TelemetryPrivacyViolationException(
          code: 'FORBIDDEN_VALUE_EMAIL',
          message: 'Potential email detected at $childPath',
        );
      }
    }
  }
}

class TelemetryPrivacyViolationException implements Exception {
  final String code;
  final String message;

  const TelemetryPrivacyViolationException({
    required this.code,
    required this.message,
  });

  @override
  String toString() => 'TelemetryPrivacyViolationException($code): $message';
}
