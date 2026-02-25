## Summary

- What changed:
- Why:

## Validation Checklist

- [ ] `powershell -ExecutionPolicy Bypass -File scripts/unity_export_authoring_contracts.ps1`
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/authoring_contract_gate.ps1`
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/unity_session_flow_validation_pack.ps1 -SkipCompile`
- [ ] `cd flutter_controller && flutter analyze`
- [ ] `cd flutter_controller && flutter test test/widget_test.dart test/game_catalog_service_test.dart test/mobile_control_schema_test.dart`
- [ ] `cd admin_console_web && flutter analyze`
- [ ] `cd admin_console_web && flutter test`

## Notes

- Related docs updated (if contract/flow changed):
- Any follow-up tasks:
