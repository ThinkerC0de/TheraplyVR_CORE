import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/operator_incident_popup_queue.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('OperatorIncidentPopupQueue', () {
    test('buildOperatorIncidentHistoryReport formats ordered sequence', () {
      final alerts = <OperatorIncidentAlert>[
        OperatorIncidentAlert(
          occurredAtUtc: DateTime.utc(2026, 2, 22, 18, 0, 0),
          source: 'control_screen',
          title: 'Session attach failed',
          message: 'Attach failed after retries.',
          reasonCode: 'SESSION_LOCK_CONFLICT',
          severity: OperatorIncidentSeverity.error,
        ),
        OperatorIncidentAlert(
          occurredAtUtc: DateTime.utc(2026, 2, 22, 18, 1, 0),
          source: 'students_screen',
          title: 'Write queued',
          message: 'Pending writes waiting for network.',
          reasonCode: 'TCP_LINK_LOST',
          severity: OperatorIncidentSeverity.warning,
        ),
      ];

      final report = buildOperatorIncidentHistoryReport(alerts);

      expect(report, contains('1.'));
      expect(report, contains('2.'));
      expect(report, contains('E-1201 (SESSION_LOCK_CONFLICT)'));
      expect(report, contains('E-2109 (TCP_LINK_LOST)'));
      expect(report, contains('Attach failed after retries.'));
      expect(report, contains('Pending writes waiting for network.'));
    });

    test('buildOperatorIncidentHistoryReport handles empty list', () {
      final report = buildOperatorIncidentHistoryReport(
        const <OperatorIncidentAlert>[],
      );

      expect(report, 'No incident alerts captured.');
    });

    test('buildOperatorIncidentEmailDraft contains report metadata', () {
      final alerts = <OperatorIncidentAlert>[
        OperatorIncidentAlert(
          occurredAtUtc: DateTime.utc(2026, 2, 22, 18, 0, 0),
          source: 'control_screen',
          title: 'Session attach failed',
          message: 'Attach failed after retries.',
          reasonCode: 'SESSION_LOCK_CONFLICT',
          severity: OperatorIncidentSeverity.error,
        ),
      ];

      final draft = buildOperatorIncidentEmailDraft(
        queueName: 'control_screen',
        reportId: 'rpt-123',
        generatedAtUtc: DateTime.utc(2026, 2, 22, 18, 2, 0),
        alerts: alerts,
      );

      expect(draft.subject, contains('rpt-123'));
      expect(draft.subject, contains('control_screen'));
      expect(draft.body, contains('Report ID: rpt-123'));
      expect(draft.body, contains('Event sequence:'));
      expect(draft.body, contains('SESSION_LOCK_CONFLICT'));
    });

    test('buildOperatorIncidentEmailDraft supports polish language option', () {
      final alerts = <OperatorIncidentAlert>[
        OperatorIncidentAlert(
          occurredAtUtc: DateTime.utc(2026, 2, 22, 18, 0, 0),
          source: 'control_screen',
          title: 'Session attach failed',
          message: 'Attach failed after retries.',
          reasonCode: 'SESSION_LOCK_CONFLICT',
          severity: OperatorIncidentSeverity.error,
        ),
      ];

      final draft = buildOperatorIncidentEmailDraft(
        queueName: 'control_screen',
        reportId: 'rpt-pl-1',
        generatedAtUtc: DateTime.utc(2026, 2, 22, 18, 2, 0),
        alerts: alerts,
        language: TherapistUiLanguage.polish,
      );

      expect(draft.body, contains('Sekwencja zdarzen:'));
      expect(draft.body, contains('Incydenty: 1'));
    });
  });
}
