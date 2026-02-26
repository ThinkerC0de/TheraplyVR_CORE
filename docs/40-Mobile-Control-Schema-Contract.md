# Mobile Control Schema Contract

Date: 2026-02-25  
Status: active contract (Unity + Flutter parser/validator implemented).

## Purpose

Define one declarative schema used by content authoring and the mobile controller so setup controls are rendered without hardcoded per-game UI branches.

## Top-level envelope

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `schema` | string | yes | Must equal `THERAPLY_MOBILE_CONTROL_SCHEMA`. |
| `schemaVersion` | string | yes | Contract version string, e.g. `2026-02-25`. |
| `gameId` | string | yes | Must match game definition/catalog game id. |
| `title` | string | no | Setup panel title. |
| `description` | string | no | Optional helper text. |
| `layout` | object | no | Layout metadata (`mode`, `columns`). |
| `payload` | object | no | Payload metadata (`target`, `gameConfigType`, `gameConfigVersion`, `includeVersionInGameConfig`). |
| `sections` | array | no | Optional section grouping for controls. |
| `controls` | array | yes | List of control definitions. |

## Layout contract

| Field | Type | Required | Allowed |
| --- | --- | --- | --- |
| `layout.mode` | string | no | `stack`, `grid` |
| `layout.columns` | int | no | `>= 1` |

## Payload contract

| Field | Type | Required | Allowed |
| --- | --- | --- | --- |
| `payload.target` | string | no | `game_config` |
| `payload.gameConfigType` | string | no | Emitted as `gameConfigType` in `START_GAME`/`UPDATE_CONFIG`. |
| `payload.gameConfigVersion` | int | no | Emitted as `gameConfigVersion`. |
| `payload.includeVersionInGameConfig` | bool | no | If true, renderer injects `version` into `gameConfigJson`. |

## Section contract

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `sectionId` | string | yes | Unique per schema. |
| `label` | string | no | UI label. |
| `order` | int | no | Sort key. |

## Control contract

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `controlId` | string | yes | Unique per schema. |
| `type` | string | yes | `slider`, `toggle`, `select`, `number`, `text`, `button`. |
| `label` | string | no | UI label. |
| `hint` | string | no | Optional subtitle/help. |
| `placeholder` | string | no | For text/number. |
| `sectionId` | string | no | Must reference existing section. |
| `order` | int | no | Sort key. |
| `defaultValue` | string | no | String-encoded default. |
| `buttonCommandId` | string | required for `button` | Command emitted by button control (typically `UPDATE_CONFIG`). |
| `binding` | object | required for non-button | Maps UI value into payload config key. |
| `validation` | object | no | Required/range/length/regex metadata. |
| `visual` | object | no | Optional authored card placement metadata (`x`, `y`, `width`, `height`, `scale`). |
| `options` | array | required for `select` | Select options. |

## Binding contract

| Field | Type | Required | Allowed |
| --- | --- | --- | --- |
| `target` | string | no | `game_config` |
| `path` | string | yes | Config key emitted into `gameConfigJson`. |
| `valueType` | string | no | `int`, `double`, `bool`, `string` |
| `emitOnStartGame` | bool | no | Include in `START_GAME` payload. |
| `emitOnUpdateConfig` | bool | no | Include in `UPDATE_CONFIG` payload. |

## Validation contract

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `required` | bool | no | Input must be non-empty. |
| `minValue` | string | no | Numeric lower bound (string-encoded). |
| `maxValue` | string | no | Numeric upper bound (string-encoded). |
| `step` | string | no | Suggested increment (string-encoded). |
| `minLength` | int | no | Text minimum length. |
| `maxLength` | int | no | Text maximum length. |
| `regex` | string | no | Regex pattern for text inputs. |

## Visual contract (optional)

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `x` | float | no | Normalized X in card grid; `< 0` means auto placement. |
| `y` | float | no | Normalized Y in card grid; `< 0` means auto placement. |
| `width` | float | no | Normalized width `(0, 1]`. |
| `height` | float | no | Normalized height `(0, 1]`. |
| `scale` | float | no | UI scale multiplier `(0, 4]`. |

## Canonical validation reason codes

- `MOBILE_SCHEMA_NULL`
- `MOBILE_SCHEMA_ID_INVALID`
- `MOBILE_SCHEMA_VERSION_REQUIRED`
- `MOBILE_SCHEMA_GAME_ID_REQUIRED`
- `MOBILE_SCHEMA_GAME_ID_MISMATCH`
- `MOBILE_SCHEMA_CONTROLS_REQUIRED`
- `MOBILE_SCHEMA_LAYOUT_MODE_UNSUPPORTED`
- `MOBILE_SCHEMA_SECTION_ID_REQUIRED`
- `MOBILE_SCHEMA_SECTION_ID_DUPLICATE`
- `MOBILE_SCHEMA_CONTROL_ID_REQUIRED`
- `MOBILE_SCHEMA_CONTROL_ID_DUPLICATE`
- `MOBILE_SCHEMA_CONTROL_TYPE_UNSUPPORTED`
- `MOBILE_SCHEMA_BINDING_REQUIRED`
- `MOBILE_SCHEMA_BINDING_PATH_REQUIRED`
- `MOBILE_SCHEMA_BINDING_TARGET_UNSUPPORTED`
- `MOBILE_SCHEMA_BINDING_VALUE_TYPE_UNSUPPORTED`
- `MOBILE_SCHEMA_SELECT_OPTIONS_REQUIRED`
- `MOBILE_SCHEMA_SELECT_OPTION_VALUE_REQUIRED`
- `MOBILE_SCHEMA_SECTION_REFERENCE_MISSING`
- `MOBILE_SCHEMA_RANGE_INVALID`
- `MOBILE_SCHEMA_VISUAL_INVALID`
- `MOBILE_SCHEMA_BUTTON_COMMAND_REQUIRED`

## Reference implementation paths

- Unity contracts + validator:
  - `unity-quest-template/Assets/_TheraplyCore/Games/Contracts/MobileControlSchemaContracts.cs`
- Unity game definition contract integration:
  - `unity-quest-template/Assets/_TheraplyCore/Games/Contracts/SessionFlowContracts.cs`
- Flutter parser + validator:
  - `flutter_controller/lib/models/mobile_control_schema.dart`
- Flutter catalog integration:
  - `flutter_controller/lib/models/game_catalog_entry.dart`
