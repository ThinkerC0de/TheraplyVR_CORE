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
  static CollectionReference<Map<String, dynamic>> get _settingsCollection =>
      _firestore.collection('therapist_session_settings');
  static CollectionReference<Map<String, dynamic>>
      get _legacyEntitlementsCollection =>
          _firestore.collection('user_entitlements');

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
    return fetchSettingsForTherapistId(therapistId);
  }

  static Future<TherapistSessionSettings> fetchSettingsForTherapistId(
    String therapistId, {
    bool publishResult = true,
  }) async {
    final normalizedTherapistId = therapistId.trim();
    if (normalizedTherapistId.isEmpty) {
      final defaults = TherapistSessionSettings.defaults();
      if (publishResult) {
        _publishSettings(defaults);
      }
      return defaults;
    }

    try {
      final settingsSnapshot =
          await _settingsCollection.doc(normalizedTherapistId).get();
      if (settingsSnapshot.exists) {
        final settings =
            TherapistSessionSettings.fromMap(settingsSnapshot.data());
        if (publishResult) {
          _publishSettings(settings);
        }
        return settings;
      }

      final legacySnapshot =
          await _legacyEntitlementsCollection.doc(normalizedTherapistId).get();
      if (legacySnapshot.exists) {
        final settings =
            TherapistSessionSettings.fromMap(legacySnapshot.data());
        if (publishResult) {
          _publishSettings(settings);
        }
        return settings;
      }

      final defaults = TherapistSessionSettings.defaults();
      if (publishResult) {
        _publishSettings(defaults);
      }
      return defaults;
    } catch (e) {
      debugPrint(
        '[TherapistSessionSettingsService] Failed to load therapist settings: $e',
      );
      final defaults = TherapistSessionSettings.defaults();
      if (publishResult) {
        _publishSettings(defaults);
      }
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
      await _settingsCollection.doc(therapistId).set(
        <String, dynamic>{
          'therapistId': therapistId,
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
