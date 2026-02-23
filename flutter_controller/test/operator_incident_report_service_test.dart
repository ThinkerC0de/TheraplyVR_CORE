import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/services/operator_incident_report_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('OperatorIncidentReportService', () {
    late FakeFirebaseFirestore firestore;

    setUp(() {
      firestore = FakeFirebaseFirestore();
      OperatorIncidentReportService.setFirestoreInstanceForTesting(firestore);
      OperatorIncidentReportService.setActorForTesting(
        actorUid: 'therapist-ops',
        actorEmail: 'therapist@example.com',
      );
    });

    tearDown(() {
      OperatorIncidentReportService.clearTestingOverrides();
    });

    test('submitIncidentReport writes normalized payload', () async {
      final reportId = await OperatorIncidentReportService.submitIncidentReport(
        queueName: 'control_screen',
        generatedAtUtc: DateTime.utc(2026, 2, 22, 20, 10, 0),
        historyReport:
            '1. [2026-02-22T20:10:00.000Z] E-1201 (SESSION_LOCK_CONFLICT)',
        context: <String, dynamic>{
          'screen': 'control_screen',
          'runtimeState': 'IN_PROGRESS',
          'capturedAt': DateTime.utc(2026, 2, 22, 20, 11, 0),
        },
        incidents: <Map<String, dynamic>>[
          <String, dynamic>{
            'sequence': 1,
            'occurredAtUtc': DateTime.utc(2026, 2, 22, 20, 10, 0),
            'source': 'control_screen',
            'title': 'Session attach failed',
            'message': 'Attach failed after 3 retries',
            'reasonCode': 'SESSION_LOCK_CONFLICT',
            'severity': 'error',
            'context': <String, dynamic>{
              'sessionId': 'mobile-1',
              'retry': 3,
            },
          },
          <String, dynamic>{
            'sequence': 2,
            'occurredAtUtc': DateTime.utc(2026, 2, 22, 20, 11, 0),
            'source': 'students_screen',
            'title': 'Write queued',
            'message': 'Pending writes waiting for network',
            'reasonCode': 'TCP_LINK_LOST',
            'severity': 'warning',
            'context': <String, dynamic>{
              'pendingWrites': 2,
            },
          },
        ],
      );

      final snapshot = await firestore
          .collection('operator_incident_reports')
          .doc(reportId)
          .get();
      expect(snapshot.exists, isTrue);

      final payload = snapshot.data()!;
      expect(payload['reportId'], reportId);
      expect(payload['actorUid'], 'therapist-ops');
      expect(payload['actorEmail'], 'therapist@example.com');
      expect(payload['queueName'], 'control_screen');
      expect(payload['incidentCount'], 2);
      expect(
        payload['incidentReasonCodes'],
        <String>['SESSION_LOCK_CONFLICT', 'TCP_LINK_LOST'],
      );
      expect(payload['historyReport'], contains('SESSION_LOCK_CONFLICT'));

      final severityBreakdown = Map<String, dynamic>.from(
        payload['severityBreakdown'] as Map<String, dynamic>,
      );
      expect(severityBreakdown['error'], 1);
      expect(severityBreakdown['warning'], 1);
      expect(severityBreakdown['info'], 0);
      expect(severityBreakdown['unknown'], 0);

      final context = Map<String, dynamic>.from(
        payload['context'] as Map<String, dynamic>,
      );
      expect(context['screen'], 'control_screen');
      expect(context['runtimeState'], 'IN_PROGRESS');
      expect(context['capturedAt'], '2026-02-22T20:11:00.000Z');

      final incidents = (payload['incidents'] as List<dynamic>)
          .map((entry) => Map<String, dynamic>.from(entry as Map))
          .toList(growable: false);
      expect(incidents, hasLength(2));
      expect(incidents[0]['reasonCode'], 'SESSION_LOCK_CONFLICT');
      expect(incidents[1]['reasonCode'], 'TCP_LINK_LOST');

      final firstContext = Map<String, dynamic>.from(
        incidents[0]['context'] as Map<String, dynamic>,
      );
      expect(firstContext['sessionId'], 'mobile-1');
      expect(firstContext['retry'], 3);
    });

    test('submitIncidentReport fails when actor uid is missing', () async {
      OperatorIncidentReportService.setActorForTesting(actorUid: ' ');

      await expectLater(
        () => OperatorIncidentReportService.submitIncidentReport(
          queueName: 'control_screen',
          generatedAtUtc: DateTime.utc(2026, 2, 22, 20, 10, 0),
          historyReport: 'x',
          incidents: const <Map<String, dynamic>>[],
        ),
        throwsStateError,
      );
    });
  });
}
