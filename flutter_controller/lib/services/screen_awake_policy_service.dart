import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/therapist_session_settings_service.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

class ScreenAwakePolicyService with WidgetsBindingObserver {
  ScreenAwakePolicyService._();

  static final ScreenAwakePolicyService instance = ScreenAwakePolicyService._();

  StreamSubscription<TherapistSessionSettings>? _settingsSubscription;
  AppLifecycleState _lifecycleState = AppLifecycleState.resumed;
  bool _keepScreenAwakeWhenForeground = false;
  bool _started = false;

  void start() {
    if (_started) {
      return;
    }

    _started = true;
    _lifecycleState =
        WidgetsBinding.instance.lifecycleState ?? AppLifecycleState.resumed;
    _keepScreenAwakeWhenForeground = TherapistSessionSettingsService
        .latestKnownSettings.keepScreenAwakeWhenForeground;
    WidgetsBinding.instance.addObserver(this);
    _settingsSubscription =
        TherapistSessionSettingsService.watchSettingsUpdates().listen((
      settings,
    ) {
      _keepScreenAwakeWhenForeground = settings.keepScreenAwakeWhenForeground;
      unawaited(_applyPolicy());
    });
    unawaited(_applyPolicy());
  }

  Future<void> stop() async {
    if (!_started) {
      return;
    }

    _started = false;
    WidgetsBinding.instance.removeObserver(this);
    await _settingsSubscription?.cancel();
    _settingsSubscription = null;
    await WakelockPlus.disable();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _lifecycleState = state;
    unawaited(_applyPolicy());
  }

  Future<void> _applyPolicy() async {
    if (!_started) {
      return;
    }

    final keepAwake = _keepScreenAwakeWhenForeground &&
        _lifecycleState == AppLifecycleState.resumed;

    try {
      await WakelockPlus.toggle(enable: keepAwake);
    } catch (_) {
      // Keep app flow resilient if wake lock cannot be changed.
    }
  }
}
