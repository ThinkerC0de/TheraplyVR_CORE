import 'dart:convert';

import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses GAME_INSTALL_STATUS into purchased content state', () {
    final updatedAt = DateTime.utc(2026, 2, 18, 11, 15, 0);
    final message = <String, dynamic>{
      'commandId': ContentDeliveryCommandIds.gameInstallStatus,
      'payload': base64Encode(
        utf8.encode(
          jsonEncode(<String, dynamic>{
            'gameId': 'demo_cube_clicker',
            'owned': true,
            'installedVersion': '1.0.0',
            'targetVersion': '1.1.0',
            'updateRequired': true,
            'updateOptional': false,
            'runtimeStatus': 'UPDATE_REQUIRED',
            'lastError': null,
            'updatedAtUtc': updatedAt.toIso8601String(),
          }),
        ),
      ),
    };

    final signal = ContentInstallStatusSignal.tryFromNetworkMessage(message);

    expect(signal, isNotNull);
    expect(signal!.state.gameId, 'demo_cube_clicker');
    expect(signal.state.owned, isTrue);
    expect(signal.state.installedVersion, '1.0.0');
    expect(signal.state.targetVersion, '1.1.0');
    expect(signal.state.runtimeStatus, ContentRuntimeStatus.updateRequired);
    expect(signal.state.updatedAtUtc, updatedAt);
  });

  test('runtime status codec falls back to NOT_INSTALLED', () {
    expect(
      ContentRuntimeStatusCodec.fromWire('SYNCING_MANIFEST'),
      ContentRuntimeStatus.syncingManifest,
    );
    expect(
      ContentRuntimeStatusCodec.fromWire('DOWNLOADING'),
      ContentRuntimeStatus.downloading,
    );
    expect(
      ContentRuntimeStatusCodec.fromWire('VERIFYING'),
      ContentRuntimeStatus.verifying,
    );
    expect(
      ContentRuntimeStatusCodec.fromWire('ACTIVATING'),
      ContentRuntimeStatus.activating,
    );
    expect(
      ContentRuntimeStatusCodec.fromWire('ROLLING_BACK'),
      ContentRuntimeStatus.rollingBack,
    );
    expect(
      ContentRuntimeStatusCodec.fromWire('unknown_status'),
      ContentRuntimeStatus.notInstalled,
    );
    expect(ContentRuntimeStatus.ready.wireValue, 'READY');
  });

  test('request payload builder includes required fields', () {
    final timestamp = DateTime.utc(2026, 2, 18, 12, 0, 0);
    final payload = ContentDeliveryRequests.buildInstallRequest(
      actorId: 'therapist-1',
      gameId: 'pulse_target_tap',
      targetVersion: '2.0.0',
      issuedAtUtc: timestamp,
    );

    expect(payload['actorId'], 'therapist-1');
    expect(payload['gameId'], 'pulse_target_tap');
    expect(payload['targetVersion'], '2.0.0');
    expect(payload['issuedAtUtc'], timestamp.toIso8601String());
  });

  test('request install transition starts with syncing manifest phase', () {
    expect(
      ContentDeliveryTransitionRule.nextStatus(
        current: ContentRuntimeStatus.notInstalled,
        action: ContentDeliveryAction.requestInstallOrUpdate,
      ),
      ContentRuntimeStatus.syncingManifest,
    );
  });

  test('launch gate requires quest status signal when delivery is enabled', () {
    final readyState = PurchasedContentState(
      gameId: 'demo_cube_clicker',
      owned: true,
      installedVersion: '1.2.0',
      targetVersion: '1.2.0',
      updateRequired: false,
      updateOptional: false,
      runtimeStatus: ContentRuntimeStatus.ready,
      lastError: null,
      updatedAtUtc: DateTime.utc(2026, 2, 25, 17, 0, 0),
    );

    expect(
      ContentLaunchGate.isLaunchable(
        state: readyState,
        contentDeliveryEnabled: true,
        hasQuestStatusSignal: false,
      ),
      isFalse,
    );
    expect(
      ContentLaunchGate.isLaunchable(
        state: readyState,
        contentDeliveryEnabled: true,
        hasQuestStatusSignal: true,
      ),
      isTrue,
    );
  });

  test('launch gate blocks READY state flagged as updateRequired', () {
    final updateRequiredState = PurchasedContentState(
      gameId: 'demo_cube_clicker',
      owned: true,
      installedVersion: '1.1.0',
      targetVersion: '1.2.0',
      updateRequired: true,
      updateOptional: false,
      runtimeStatus: ContentRuntimeStatus.ready,
      lastError: null,
      updatedAtUtc: DateTime.utc(2026, 2, 25, 17, 1, 0),
    );

    expect(
      ContentLaunchGate.isLaunchable(
        state: updateRequiredState,
        contentDeliveryEnabled: true,
        hasQuestStatusSignal: true,
      ),
      isFalse,
    );
  });
}
