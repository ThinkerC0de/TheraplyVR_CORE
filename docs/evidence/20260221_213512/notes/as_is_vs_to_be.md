# AS-IS vs TO-BE (Quick Stabilizer)

## AS-IS
- END_SESSION could be blocked by attach precondition (Flutter) and session lock conflict (Unity).
- Handoff popup could trigger from persisted/runtime signals outside entry/reconnect context.
- Dialog behavior/copy was mixed PL/EN and back-dismiss paths were not deterministic.
- Logs were insufficient to explain handoff/attach decision paths.

## TO-BE
- END_SESSION bypasses attach precondition and session lock mismatch.
- Handoff popup appears only in entry/reconnect context and is suppressed in stable active session flow.
- Handoff/start-new/back-close dialogs use consistent EN copy with explicit actions.
- Decision traces clearly explain show/suppress and attach required/bypassed states.
