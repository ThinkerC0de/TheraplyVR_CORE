import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_controller/models/pseudonymization_contract.dart';
import 'package:flutter_controller/services/telemetry_privacy_guard.dart';

void main() {
  SessionTelemetryPayload buildPayload({
    Map<String, dynamic>? metrics,
  }) {
    return SessionTelemetryPayload(
      schemaVersion: '2026-02-18',
      mode: PseudonymizationMode.clinicalResearch,
      tenantId: 'tenant-a',
      sessionPseudoId: 'sess-pseudo-1',
      therapistPseudoId: 'ther-pseudo-1',
      studentPseudoId: 'stu-pseudo-1',
      gameId: 'demo_cube_clicker',
      eventAtUtc: DateTime.parse('2026-02-18T12:00:00Z'),
      eventType: 'game_start',
      metrics: metrics ?? <String, dynamic>{'score': 12, 'latencyMs': 24},
      pseudonymKeyRef: 'kms://region/key-1',
      algorithmTag: 'HMAC_SHA256_V1',
    );
  }

  test('allows telemetry payload without direct identifiers', () {
    final payload = buildPayload();
    final safeMap = TelemetryPrivacyGuard.validateForDispatch(payload);

    expect(safeMap['eventType'], 'game_start');
    expect(safeMap['studentPseudoId'], 'stu-pseudo-1');
  });

  test('blocks forbidden key in nested metrics map', () {
    final payload = buildPayload(
      metrics: <String, dynamic>{
        'score': 12,
        'firstName': 'Anna',
      },
    );

    expect(
      () => TelemetryPrivacyGuard.validateForDispatch(payload),
      throwsA(
        isA<TelemetryPrivacyViolationException>()
            .having((e) => e.code, 'code', 'FORBIDDEN_KEY'),
      ),
    );
  });

  test('blocks email-like value in nested metrics map', () {
    final payload = buildPayload(
      metrics: <String, dynamic>{
        'errorContext': <String, dynamic>{
          'rawValue': 'anna.kowalska@example.com',
        },
      },
    );

    expect(
      () => TelemetryPrivacyGuard.validateForDispatch(payload),
      throwsA(
        isA<TelemetryPrivacyViolationException>()
            .having((e) => e.code, 'code', 'FORBIDDEN_VALUE_EMAIL'),
      ),
    );
  });

  test('blocks forbidden internal id key in metrics map', () {
    final payload = buildPayload(
      metrics: <String, dynamic>{
        'studentId': 'student-raw-id',
      },
    );

    expect(
      () => TelemetryPrivacyGuard.validateForDispatch(payload),
      throwsA(
        isA<TelemetryPrivacyViolationException>()
            .having((e) => e.code, 'code', 'FORBIDDEN_KEY'),
      ),
    );
  });
}
