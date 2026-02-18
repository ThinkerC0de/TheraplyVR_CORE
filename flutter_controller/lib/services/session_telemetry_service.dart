import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter_controller/models/pseudonymization_contract.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/telemetry_privacy_guard.dart';

class SessionTelemetryService {
  static final CollectionReference<Map<String, dynamic>> _telemetryCollection =
      FirebaseService.firestore.collection('session_telemetry');

  static Future<void> dispatchSessionTelemetry(
    SessionTelemetryPayload payload,
  ) async {
    final safePayload = TelemetryPrivacyGuard.validateForDispatch(payload);
    await _telemetryCollection.add(safePayload);
  }
}
