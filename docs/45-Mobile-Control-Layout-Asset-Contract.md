# Mobile Control Layout Asset Contract

Date: 2026-02-26  
Status: active authoring contract (Unity asset + Unity validator + Flutter parser + Flutter catalog bridge).

## Purpose

Define one Unity-authored card layout contract that captures control semantics and placement metadata, then converts safely to runtime `THERAPLY_MOBILE_CONTROL_SCHEMA`.

Flutter catalog resolution order:

1. `mobileControlLayout` (preferred, validated, converted to schema)
2. `mobileControlSchema` (legacy fallback)

## Contract identity

| Field | Value |
| --- | --- |
| `schema` | `THERAPLY_MOBILE_CONTROL_LAYOUT` |
| `schemaVersion` | `2026-02-26` (current) |

## Top-level fields

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `gameId` | string | yes | Must match game/catalog id. |
| `title` | string | no | Mobile card title. |
| `description` | string | no | Optional helper text. |
| `layout.mode` | string | no | `stack`, `grid`. |
| `layout.columns` | int | no | `>= 1`. |
| `payload` | object | no | `target`, `gameConfigType`, `gameConfigVersion`, `includeVersionInGameConfig`. |
| `sections` | array | no | Optional logical grouping. |
| `controls` | array | yes | Authored control list. |

## Control fields

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `controlId` | string | yes | Unique per layout. |
| `type` | string | yes | `slider`, `toggle`, `select`, `number`, `text`, `button`. |
| `label` | string | no | UI label. |
| `description` | string | no | UI helper text. |
| `bindingKey` | string | yes (non-button) | Key mapped to `gameConfigJson`. |
| `valueType` | string | no | `int`, `double`, `bool`, `string`. |
| `buttonCommandId` | string | yes (button) | Usually `UPDATE_CONFIG`. |
| `visual` | object | no | `x`, `y`, `width`, `height`, `scale` placement metadata. |
| `minValue/maxValue/step/minLength/maxLength/regex` | mixed | no | Validation metadata. |
| `options` | array | yes (`select`) | Select options. |

## Canonical validation reason codes

- `MOBILE_LAYOUT_NULL`
- `MOBILE_LAYOUT_SCHEMA_ID_INVALID`
- `MOBILE_LAYOUT_SCHEMA_VERSION_REQUIRED`
- `MOBILE_LAYOUT_GAME_ID_REQUIRED`
- `MOBILE_LAYOUT_GAME_ID_MISMATCH`
- `MOBILE_LAYOUT_MODE_UNSUPPORTED`
- `MOBILE_LAYOUT_CONTROLS_REQUIRED`
- `MOBILE_LAYOUT_SECTION_ID_REQUIRED`
- `MOBILE_LAYOUT_SECTION_ID_DUPLICATE`
- `MOBILE_LAYOUT_SECTION_REFERENCE_MISSING`
- `MOBILE_LAYOUT_CONTROL_ID_REQUIRED`
- `MOBILE_LAYOUT_CONTROL_ID_DUPLICATE`
- `MOBILE_LAYOUT_CONTROL_TYPE_UNSUPPORTED`
- `MOBILE_LAYOUT_BINDING_KEY_REQUIRED`
- `MOBILE_LAYOUT_BINDING_TARGET_UNSUPPORTED`
- `MOBILE_LAYOUT_BINDING_VALUE_TYPE_UNSUPPORTED`
- `MOBILE_LAYOUT_BUTTON_COMMAND_REQUIRED`
- `MOBILE_LAYOUT_SELECT_OPTIONS_REQUIRED`
- `MOBILE_LAYOUT_SELECT_OPTION_VALUE_REQUIRED`
- `MOBILE_LAYOUT_RANGE_INVALID`
- `MOBILE_LAYOUT_VISUAL_INVALID`

## Reference implementation paths

- Unity asset + validator + converter:
  - `unity-quest-template/Assets/_TheraplyCore/Games/Contracts/MobileControlLayoutContracts.cs`
- Unity editor window:
  - `unity-quest-template/Assets/_TheraplyCore/Editor/Authoring/MobileControlLayoutEditorWindow.cs`
  - menu: `Theraply > Scene Controller > Mobile Layout Editor`
- Unity schema contract extension (`visual` metadata):
  - `unity-quest-template/Assets/_TheraplyCore/Games/Contracts/MobileControlSchemaContracts.cs`
- Flutter parser:
  - `flutter_controller/lib/models/mobile_control_layout_contract.dart`
- Flutter catalog/runtime wiring:
  - `flutter_controller/lib/models/game_catalog_entry.dart`
  - `flutter_controller/lib/screens/control_screen.dart`
