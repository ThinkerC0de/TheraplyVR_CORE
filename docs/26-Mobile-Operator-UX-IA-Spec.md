# Mobile Operator UX/IA Spec (Iteration 1)

Date: 2026-02-18
Scope: `flutter_controller` operator flow (`login` -> `students` -> `control` with `gameCatalog` and `gameSetup`).

## 1. Screen Map

1. Login (`login_screen.dart`)
2. Students workspace (`students_screen.dart`)
3. Session control (`control_screen.dart`)
   - Step 4: Game catalog
   - Step 5: Game setup + runtime actions

## 2. Login Screen

Sections (top to bottom):
1. Brand header (product identity)
2. Local restore status (cached email / restoring)
3. Credentials form (email + password)
4. Login CTA (single primary action)
5. Entitlement/support hint

Primary CTA:
- `Login`

States:
- `loading`: disable inputs + spinner in CTA
- `restoring`: disable inputs + restore progress
- `error`: entitlement-aware error card with next step and reason code
- `offline/back-end unavailable`: represented through entitlement/backend error copy

## 3. Students Workspace

Sections (top to bottom):
1. Operator workspace card
   - account
   - role and access mode (manage or read-only)
2. Pending writes banner (only when pending > 0)
3. Student roster area
   - roster header with count
   - scrollable student cards
   - pull-to-refresh

Primary CTA:
- `Add student` (enabled only for therapist role)

Secondary CTA:
- `Sync / Reconcile`
- `Logout`
- `Open student` (tap card -> device scan)

States:
- `loading`: centered spinner
- `empty`: no students yet; role-aware instruction
- `error`: backend load failure with retry + reconcile actions
- `offline`: dedicated offline wording in error state
- `read-only`: management actions disabled and labeled

## 4. Control Screen IA

Global sections:
1. Connection/session diagnostics header
2. Workflow step indicator (`Game catalog` / `Game setup`)
3. Active step content
4. Back-to-students CTA

### 4.1 Step 4: Game Catalog

Sections:
1. Catalog header + sync action
2. Runtime guard banner (if game already active)
3. Game cards with:
   - title + description
   - content readiness chips (`status`, `owned`, `version`)
   - expandable preview and error details
   - install/update/uninstall actions
4. Continue CTA to setup

Primary CTA:
- `Configure selected game`

States:
- `loading`: sync button progress
- `empty`: no games placeholder (future-proof)
- `error`: per-game install error surfaced in card
- `offline`: actions disabled by connection status + explicit banner
- `active game`: runtime warning banner; no setup edits in next step

### 4.2 Step 5: Game Setup + Runtime

Sections:
1. Selected game summary
2. Content readiness block
3. Game-specific settings block (or generic fallback)
4. Preview + save/resume support hint
5. Setup actions (`Start`, `Restart`, `Back`)
6. Runtime actions (`Pause`, `Resume`, `Stop`, `End Session`, `Manual Re-sync`)

Primary CTA:
- `Start` when runtime inactive and game launchable

Secondary CTA:
- `Restart` when runtime active
- Runtime command buttons (state-gated)

States:
- `loading`: primary action lock while command in flight
- `error`: command failure snackbars + content error labels
- `offline`: command buttons disabled
- `active game`: setup inputs locked; runtime controls enabled

## 5. Non-Negotiable Runtime Guardrails

1. Do not allow setup mutation during active runtime.
2. Keep state-gated command availability (`PAUSE/RESUME/STOP/END_SESSION`).
3. Keep session decision gate behavior and recovery policy.
4. Do not regress content readiness checks before `Start`.

## 6. Iteration 1 Delivery Target

1. Implement IA polish for `login` and `students` first.
2. Implement control step visual/state clarity without changing command semantics.
3. Validate with `flutter analyze` + `flutter test` after each major UI batch.

## 7. Iteration 2 UX Delta (2026-02-18)

1. Students is the single logout location.
   - logout now requires confirmation and always routes to Login screen.
2. Students top area is simplified.
   - account + role moved into compact expandable context block.
   - roster copy changed to `Your students`.
   - refresh/reconcile kept in less invasive placement.
3. Scanner no longer exposes logout.
4. Control is split into two logical screens:
   - Screen A: game catalog (selection + install/update + readiness only),
   - Screen B: dedicated game session (preview + settings + game controls).
5. Catalog screen keeps collapsible VR preview and licensed-content hint.
   - explicit operator guidance for requesting additional games.
6. Technical session/runtime diagnostics are hidden from operator-facing IA.
7. Screen B footer action is `End Session` (instead of back-to-student CTA).
