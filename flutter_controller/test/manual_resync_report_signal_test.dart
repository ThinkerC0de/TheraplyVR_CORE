import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_controller/models/manual_resync_report_signal.dart';

void main() {
  test('parses MANUAL_RESYNC_REPORT payload', () {
    final payload = <String, dynamic>{
      'correlationId': 'corr-1',
      'success': true,
      'reasonCode': 'RESYNC_COMPLETED_IN_SYNC',
      'sessionId': 'session-123',
      'requestedBy': 'therapist-1',
      'requestedReasonCode': 'SUPPORT_MANUAL_RESYNC',
      'requestedAtUtc': '2026-02-17T10:00:00.000Z',
      'includeSyncedEvents': false,
      'targetedEvents': 4,
      'outboxRowsUpdated': 4,
      'uploadCycleTriggered': true,
      'localEvents': 20,
      'beforeReasonCode': 'MISSING_EVENTS_DETECTED',
      'beforeMissingOnServerCount': 4,
      'beforeMissingOnDeviceCount': 0,
      'beforeOutboxPending': 0,
      'afterReasonCode': 'IN_SYNC',
      'afterMissingOnServerCount': 0,
      'afterMissingOnDeviceCount': 0,
      'afterOutboxPending': 0,
      'targetedSequencePreview': '[3,4,5,6]',
      'details': 'Manual re-sync complete.',
    };

    final message = <String, dynamic>{
      'commandId': ManualResyncSignalCommandIds.manualResyncReport,
      'payload': base64Encode(utf8.encode(jsonEncode(payload))),
    };

    final signal = ManualResyncReportSignal.tryFromNetworkMessage(message);
    expect(signal, isNotNull);
    expect(signal!.success, isTrue);
    expect(signal.sessionId, 'session-123');
    expect(signal.targetedEvents, 4);
    expect(signal.afterMissingOnServerCount, 0);
  });
}
