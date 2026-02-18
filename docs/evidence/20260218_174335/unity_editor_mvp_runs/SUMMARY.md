# Unity Editor MVP Validation Runs

Date: 2026-02-18

Runs:
- firebase_validation_pulse_run1.log -> PASS
  [FirebaseNetworkValidation] PASS: gameId=pulse_target_tap; session=firebase-net-20260218171202359; onlineAccepted=32; onlineSynced=32; offlineFailureDelta=1; offlineRetryDelta=32; pendingAfterReconnect=0; reconnectAccepted=539; reconnectSynced=539
- firebase_validation_smoke_run1.log -> PASS
  [FirebaseNetworkValidation] PASS: gameId=smoke_test_game; session=firebase-net-20260218171231213; onlineAccepted=32; onlineSynced=32; offlineFailureDelta=1; offlineRetryDelta=32; pendingAfterReconnect=0; reconnectAccepted=555; reconnectSynced=555
- firebase_validation_demo_run2.log -> PASS
  [FirebaseNetworkValidation] PASS: gameId=demo_cube_clicker; session=firebase-net-20260218171248528; onlineAccepted=32; onlineSynced=32; offlineFailureDelta=1; offlineRetryDelta=32; pendingAfterReconnect=0; reconnectAccepted=571; reconnectSynced=571
- firebase_validation_demo_run3.log -> PASS
  [FirebaseNetworkValidation] PASS: gameId=demo_cube_clicker; session=firebase-net-20260218171305854; onlineAccepted=32; onlineSynced=32; offlineFailureDelta=1; offlineRetryDelta=32; pendingAfterReconnect=0; reconnectAccepted=587; reconnectSynced=587

Compile/build gate:
- commands/unity_cli_validate_post_unity_mvp.log -> PASS
