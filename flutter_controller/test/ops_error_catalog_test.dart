import 'package:flutter_controller/models/ops_error_catalog.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('OpsErrorCatalog', () {
    test('maps known reason code to stable error id', () {
      final entry = OpsErrorCatalog.lookupByReasonCode('SESSION_LOCK_CONFLICT');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-1201');
      expect(entry.reasonCode, 'SESSION_LOCK_CONFLICT');
    });

    test('extracts reason code from critical command exception text', () {
      final error = Exception(
        'Critical command SESSION_ATTACH failed after 3 attempts (reason=SESSION_LOCK_CONFLICT)',
      );

      final reasonCode = OpsErrorCatalog.tryExtractReasonCode(error);

      expect(reasonCode, 'SESSION_LOCK_CONFLICT');
    });

    test('builds fallback summary for unmapped reason code', () {
      final error = Exception(
        'Critical command END_SESSION failed after 2 attempts (reason=SOMETHING_NEW)',
      );

      final summary = OpsErrorCatalog.buildOperatorSummary(error: error);

      expect(summary, contains('E-0000 (SOMETHING_NEW)'));
      expect(summary, contains('Unmapped reason code'));
    });

    test('maps entitlement reason code to stable error id', () {
      final entry = OpsErrorCatalog.lookupByReasonCode('APP_LICENSE_INACTIVE');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-2002');
      expect(entry.reasonCode, 'APP_LICENSE_INACTIVE');
    });

    test('builds reason tag for known runtime presence reason code', () {
      final reasonTag = OpsErrorCatalog.buildReasonTag('APP_PAUSED');

      expect(reasonTag, 'E-2101 (APP_PAUSED)');
    });

    test('builds reason summary from fallback reason code when no regex match',
        () {
      final summary = OpsErrorCatalog.buildOperatorSummary(
        error: Exception('Connection lifecycle event'),
        fallbackReasonCode: 'TCP_LINK_LOST',
      );

      expect(summary, contains('E-2109 (TCP_LINK_LOST)'));
      expect(summary, contains('TCP link lost.'));
    });

    test('maps runtime attach internal failure identifier', () {
      final entry =
          OpsErrorCatalog.lookupByReasonCode('SESSION_ATTACH_CONTEXT_MISSING');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-2301');
      expect(entry.reasonCode, 'SESSION_ATTACH_CONTEXT_MISSING');
    });

    test('maps runtime game-action internal failure identifier', () {
      final entry = OpsErrorCatalog.lookupByReasonCode('STOP_GAME_FAILED');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-2313');
      expect(entry.reasonCode, 'STOP_GAME_FAILED');
    });

    test('maps entitlement runtime gate rejection identifier', () {
      final entry =
          OpsErrorCatalog.lookupByReasonCode('START_GAME_GAME_NOT_ENTITLED');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-2315');
      expect(entry.reasonCode, 'START_GAME_GAME_NOT_ENTITLED');
    });

    test('maps workflow decision reason code', () {
      final entry =
          OpsErrorCatalog.lookupByReasonCode('THERAPIST_START_NEW_DECISION');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-2520');
      expect(entry.reasonCode, 'THERAPIST_START_NEW_DECISION');
    });

    test('maps trace pipeline reason code', () {
      final entry = OpsErrorCatalog.lookupByReasonCode('TRACE_PATH_REQUIRED');

      expect(entry, isNotNull);
      expect(entry!.errorId, 'E-2416');
      expect(entry.reasonCode, 'TRACE_PATH_REQUIRED');
    });
  });
}
