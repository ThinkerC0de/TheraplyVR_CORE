import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SessionRecoveryPolicy', () {
    test('does not require decision when remote session is same as local', () {
      final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'session-1',
        remoteSessionId: 'session-1',
        remoteState: SessionLifecycleState.inProgress,
        remoteRuntimeStatus: TherapistRuntimeStatus.playing,
      );

      expect(requiresDecision, isFalse);
    });

    test('does not require decision for created remote session state', () {
      final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'local-session',
        remoteSessionId: 'quest-session',
        remoteState: SessionLifecycleState.created,
        remoteRuntimeStatus: TherapistRuntimeStatus.connected,
      );

      expect(requiresDecision, isFalse);
    });

    test('requires decision for different non-terminal remote session state',
        () {
      final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'local-session',
        remoteSessionId: 'quest-session',
        remoteState: SessionLifecycleState.interrupted,
        remoteRuntimeStatus: TherapistRuntimeStatus.interrupted,
      );

      expect(requiresDecision, isTrue);
    });

    test(
        'does not require decision for different terminal remote session state',
        () {
      final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'local-session',
        remoteSessionId: 'quest-session',
        remoteState: SessionLifecycleState.completed,
        remoteRuntimeStatus: TherapistRuntimeStatus.connected,
      );

      expect(requiresDecision, isFalse);
    });

    test(
        'requires decision from runtime status when session state is unavailable',
        () {
      final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'local-session',
        remoteSessionId: 'quest-session',
        remoteState: null,
        remoteRuntimeStatus: TherapistRuntimeStatus.paused,
      );

      expect(requiresDecision, isTrue);
    });

    test('adopts created rollover session after terminal active session state',
        () {
      final shouldAdopt =
          SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
        sessionAttachReady: true,
        requiresSessionDecision: false,
        activeSessionId: 'session-a',
        incomingSessionId: 'session-b',
        activeSessionState: SessionLifecycleState.completed,
        activeSessionRecentlyEnded: false,
      );

      expect(shouldAdopt, isTrue);
    });

    test('adopts created rollover session after recently ended active session',
        () {
      final shouldAdopt =
          SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
        sessionAttachReady: true,
        requiresSessionDecision: false,
        activeSessionId: 'session-a',
        incomingSessionId: 'session-b',
        activeSessionState: null,
        activeSessionRecentlyEnded: true,
      );

      expect(shouldAdopt, isTrue);
    });

    test('does not adopt rollover when handoff decision is pending', () {
      final shouldAdopt =
          SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
        sessionAttachReady: true,
        requiresSessionDecision: true,
        activeSessionId: 'session-a',
        incomingSessionId: 'session-b',
        activeSessionState: SessionLifecycleState.completed,
        activeSessionRecentlyEnded: true,
      );

      expect(shouldAdopt, isFalse);
    });

    test('does not adopt rollover for same session id', () {
      final shouldAdopt =
          SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
        sessionAttachReady: true,
        requiresSessionDecision: false,
        activeSessionId: 'session-a',
        incomingSessionId: 'session-a',
        activeSessionState: SessionLifecycleState.completed,
        activeSessionRecentlyEnded: true,
      );

      expect(shouldAdopt, isFalse);
    });

    test('does not adopt rollover for non-terminal active session', () {
      final shouldAdopt =
          SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
        sessionAttachReady: true,
        requiresSessionDecision: false,
        activeSessionId: 'session-a',
        incomingSessionId: 'session-b',
        activeSessionState: SessionLifecycleState.inProgress,
        activeSessionRecentlyEnded: false,
      );

      expect(shouldAdopt, isFalse);
    });
  });
}
