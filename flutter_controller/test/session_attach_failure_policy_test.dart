import 'package:flutter_controller/models/session_attach_failure_policy.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SessionAttachFailurePolicy', () {
    test('suppresses transient INITIAL_CONNECT failures in catalog workflow',
        () {
      final showPopup = SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: 'INITIAL_CONNECT',
        failureReasonCode: 'ACK_TIMEOUT',
        isGameSetupWorkflow: false,
      );

      expect(showPopup, isFalse);
    });

    test('suppresses transient AUTO_RECONNECT failures in catalog workflow',
        () {
      final showPopup = SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: 'AUTO_RECONNECT',
        failureReasonCode: 'DISCONNECTED',
        isGameSetupWorkflow: false,
      );

      expect(showPopup, isFalse);
    });

    test('keeps popup for COMMAND_PRECONDITION even for transient failure', () {
      final showPopup = SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: 'COMMAND_PRECONDITION',
        failureReasonCode: 'ACK_TIMEOUT',
        isGameSetupWorkflow: false,
      );

      expect(showPopup, isTrue);
    });

    test('keeps popup for handoff resume failures', () {
      final showPopup = SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: 'HANDOFF_RESUME',
        failureReasonCode: 'ACK_TIMEOUT',
        isGameSetupWorkflow: false,
      );

      expect(showPopup, isTrue);
    });

    test('keeps popup for non-transient lock conflict', () {
      final showPopup = SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: 'INITIAL_CONNECT',
        failureReasonCode: 'SESSION_LOCK_CONFLICT',
        isGameSetupWorkflow: false,
      );

      expect(showPopup, isTrue);
    });
  });
}
