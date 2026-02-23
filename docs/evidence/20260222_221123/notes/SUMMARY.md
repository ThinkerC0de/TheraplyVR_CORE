# Evidence Summary (OPS-003 fast-follow: incident report dispatch + language setting + rules deploy)

Date: 2026-02-22  
Roadmap lane:
- OPS-003 (IN_PROGRESS, fast-follow hardening)

## Scope completed in this evidence

1. Mobile operator incident reporting flow hardening:
   - popup keeps sequential blocking flow and sends report via mail draft to `errors@pranasense.pl`,
   - report is persisted in Firestore (`operator_incident_reports`) with `reportId` and incident sequence,
   - therapist-level language selector (`pl/en`) now controls incident/report copy,
   - default therapist UI language switched to `pl`.

2. Firestore rules release:
   - deployed updated `firestore.rules` with `operator_incident_reports` access policy.

3. Validation lane:
   - `flutter analyze` PASS,
   - targeted Flutter tests PASS.

## Commands

1. Firestore rules deploy:
   - `firebase.cmd deploy --only firestore:rules --project theraply-vr-demo`
   - log: `docs/evidence/20260222_221123/commands/firebase_deploy_firestore_rules.log`
   - markers: `docs/evidence/20260222_221123/commands/firebase_deploy_firestore_rules_markers.log`

2. Flutter analyze:
   - `flutter analyze` (workdir: `flutter_controller`)
   - log: `docs/evidence/20260222_221123/commands/flutter_controller_flutter_analyze.log`
   - markers: `docs/evidence/20260222_221123/commands/flutter_controller_flutter_analyze_markers.log`

3. Flutter targeted tests:
   - `flutter test test/operator_incident_popup_queue_test.dart test/operator_incident_report_service_test.dart test/therapist_session_settings_test.dart test/therapist_session_settings_service_test.dart`
   - log: `docs/evidence/20260222_221123/commands/flutter_controller_targeted_tests.log`
   - markers: `docs/evidence/20260222_221123/commands/flutter_controller_targeted_tests_markers.log`

## Result

- `OK` Firestore rules compiled and released to `theraply-vr-demo`.
- `OK` Flutter analyze PASS.
- `OK` Targeted Flutter tests PASS.
- `PARTIAL` OPS-003 remains IN_PROGRESS due separate open item: real Quest trace with dataset envelope still required for final closure.

## Implemented files (this lane)

- `firestore.rules`
- `flutter_controller/lib/services/operator_incident_popup_queue.dart`
- `flutter_controller/lib/services/operator_incident_report_service.dart`
- `flutter_controller/lib/models/therapist_session_settings.dart`
- `flutter_controller/lib/screens/control_screen.dart`
- `flutter_controller/lib/screens/students_screen.dart`
- `flutter_controller/pubspec.yaml`
- `flutter_controller/test/operator_incident_popup_queue_test.dart`
- `flutter_controller/test/operator_incident_report_service_test.dart`
- `flutter_controller/test/therapist_session_settings_test.dart`
- `flutter_controller/test/therapist_session_settings_service_test.dart`
