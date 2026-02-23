import 'dart:async';

import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class TherapistSessionSettingsService {
  static FirebaseFirestore? _firestoreOverride;
  static String? _therapistIdOverrideForTesting;
  static final StreamController<TherapistSessionSettings>
      _settingsUpdatesController =
      StreamController<TherapistSessionSettings>.broadcast();
  static TherapistSessionSettings? _lastKnownSettings;

  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>>
      get _entitlementsCollection => _firestore.collection('user_entitlements');

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void setTherapistIdForTesting(String? therapistId) {
    _therapistIdOverrideForTesting = therapistId;
  }

  @visibleForTesting
  static void clearTestingOverrides() {
    _firestoreOverride = null;
    _therapistIdOverrideForTesting = null;
    _lastKnownSettings = null;
  }

  static TherapistSessionSettings get latestKnownSettings =>
      _lastKnownSettings ?? TherapistSessionSettings.defaults();

  static Stream<TherapistSessionSettings> watchSettingsUpdates() =>
      _settingsUpdatesController.stream;

  static void clearCachedSettings() {
    _publishSettings(TherapistSessionSettings.defaults());
  }

  static Future<TherapistSessionSettings>
      fetchCurrentTherapistSettings() async {
    final therapistId = _resolveCurrentTherapistId();
    if (therapistId.isEmpty) {
      final defaults = TherapistSessionSettings.defaults();
      _publishSettings(defaults);
      return defaults;
    }

    try {
      final snapshot = await _entitlementsCollection.doc(therapistId).get();
      if (!snapshot.exists) {
        final defaults = TherapistSessionSettings.defaults();
        _publishSettings(defaults);
        return defaults;
      }

      final settings = TherapistSessionSettings.fromMap(snapshot.data());
      _publishSettings(settings);
      return settings;
    } catch (e) {
      debugPrint(
        '[TherapistSessionSettingsService] Failed to load therapist settings: $e',
      );
      final defaults = TherapistSessionSettings.defaults();
      _publishSettings(defaults);
      return defaults;
    }
  }

  static Future<bool> saveCurrentTherapistSettings(
    TherapistSessionSettings settings,
  ) async {
    final therapistId = _resolveCurrentTherapistId();
    if (therapistId.isEmpty) {
      return false;
    }

    try {
      final nowUtc = DateTime.now().toUtc().toIso8601String();
      await _entitlementsCollection.doc(therapistId).set(
        <String, dynamic>{
          ...settings.toMap(),
          'updatedAtUtc': nowUtc,
          'updatedBy': therapistId,
        },
        SetOptions(merge: true),
      );
      _publishSettings(settings);
      return true;
    } catch (e) {
      debugPrint(
        '[TherapistSessionSettingsService] Failed to save therapist settings: $e',
      );
      return false;
    }
  }

  static String _resolveCurrentTherapistId() {
    final override = _therapistIdOverrideForTesting;
    if (override != null) {
      return override.trim();
    }

    return FirebaseService.currentUser?.uid.trim() ?? '';
  }

  static void _publishSettings(TherapistSessionSettings settings) {
    _lastKnownSettings = settings;
    if (!_settingsUpdatesController.isClosed) {
      _settingsUpdatesController.add(settings);
    }
  }
}
