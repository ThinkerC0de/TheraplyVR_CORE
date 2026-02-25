# Game Catalog Contract

Date: 2026-02-25  
Schema: `THERAPLY_GAME_CATALOG`  
Collection: `game_catalog`

## Purpose

Define one catalog metadata contract used by:
- Flutter (`GameCatalogEntry` parsing + launch/config UX),
- admin seeding (`AdminGameCatalogSeedEntry` -> Firestore),
- Unity contract layer (`GameCatalogContractEntry`).

## Required stable fields

- `gameId` (string)
- `title` (string)
- `targetContentVersion` (string)
- `runtimeLaunchEnabled` (bool)
- `active` (bool)

## Shared metadata fields

- `sceneKey` (string)
- `contentVersion` (string)
- `entitlementKey` (string)
- `deliveryMode` (`bundled` or `on_demand`)
- `parameterSchema` (object, optional)

## Backward compatibility defaults

When legacy entries do not provide shared metadata fields:
- `sceneKey` -> `gameId`
- `contentVersion` -> `targetContentVersion`
- `entitlementKey` -> `game:{gameId}`
- `deliveryMode` -> `bundled`
- `parameterSchema` -> omitted (`null`)

These defaults are applied consistently in Flutter, admin seeding, and Unity contract normalization.

## Validation

- Contract gate script (`scripts/authoring_contract_gate.ps1`) validates:
  - catalog schema id (when present),
  - allowed `deliveryMode`,
  - `parameterSchema` object type when provided,
  - sync between `contracts/game_catalog_seed.json` and admin asset copy.
