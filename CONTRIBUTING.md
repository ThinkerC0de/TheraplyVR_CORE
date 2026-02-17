# Contributing Guidelines

## Scope
This repository contains:

- Unity runtime framework (`unity-quest-template`)
- Flutter therapist controller (`flutter_controller`)
- resilience and operations docs (`docs`)

## Pull Request Rules

1. Keep changes scoped to one objective (feature/fix/docs).
2. Do not mix unrelated refactors with behavior changes.
3. If behavior changes, include or update tests.
4. Update relevant docs when contracts, flows, or setup change.
5. For resilience program work, map changes to roadmap item IDs and update `docs/08-Session-Resilience-Worklog.md`.

## Validation Before Merge

Run from `flutter_controller`:

- `flutter analyze`
- `flutter test`
- `flutter build apk --debug`

If Unity-side behavior changed, run Unity compile/build validation in Editor or CI and report the result.

## Unity Changes

1. Keep contract compatibility in:
   - `Assets/_TheraplyCore/Games/Contracts/GameContracts.cs`
   - `Assets/_TheraplyCore/Games/Contracts/GameCommands.cs`
2. Prefer explicit scene wiring for critical runtime dependencies.
3. Avoid synchronous disk/network operations on gameplay hot paths.

## Flutter Changes

1. Preserve wire compatibility with Unity commands and envelopes.
2. Keep critical control commands on ACK/retry path.
3. Do not bypass session decision gate logic (`Resume` vs `Start New`).

## Documentation

If you add or remove docs:

1. Update `README.md` documentation links.
2. Update `docs/README.md` index.

