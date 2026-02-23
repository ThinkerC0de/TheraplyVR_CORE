# Controller Authority Resilience And Data Roadmap

## Cel
Stworzyc jeden kanoniczny plan doprowadzenia projektu do stanu:
- odporny na przerwy polaczenia i restarty urzadzen,
- bez utraty danych sesji i danych badawczych,
- skalowalny pod kolejne gry i przyszle wsparcie AI.

Ten dokument jest glownym punktem odniesienia do planowania, implementacji i kontroli postepu.

## Niezmienne zasady architektoniczne
1. Mobilka jest `source of truth` dla sesji, decyzji terapeuty i poprawnosci danych.
2. Unity wykonuje komendy i emituje eventy przebiegu rozgrywki.
3. Tozsamosc sesji zawsze opiera sie o:
- `ownerKey = therapistId + studentId`
- `sessionKey = therapistId + studentId + sessionId`
4. Inny terapeuta lub inne dziecko nigdy nie przejmuje aktywnej/przerwanej sesji.
5. Kazdy override lub automatyczne zamkniecie sesji musi zostawic wpis audytowy.
6. Bez suffixow `v1`, `v2` i podobnych w nazwach klas, plikow, commandow, eventow i pol.
7. Wersjonowanie kontraktow tylko w komentarzach i dokumentacji.

## Ustawienia terapeuty (gear w Students)
Zapisywane per `therapistId` w Firebase.

| Ustawienie UI | Klucz | Domyslnie | Zakres | Cel |
|---|---|---:|---|---|
| Session Recovery Window (min) | `sessionRecoveryWindowMinutes` | 60 | 5-240 | Ciche automatyczne proby wznowienia |
| Interrupted Session Auto-Close (h) | `interruptedSessionAutoCloseHours` | 48 | 1-168 | Domkniecie porzuconych sesji |
| Enable Auto-Close Interrupted Sessions | `autoCloseInterruptedSessionsEnabled` | true | true/false | Wlaczenie automatycznego zamykania |
| Require Confirmation After Recovery Window | `requireResumeConfirmationAfterRecoveryWindow` | true | true/false | Wymuszenie decyzji terapeuty po oknie recovery |
| Critical Command Retry Count | `criticalCommandMaxRetries` | 3 | 1-8 | Stabilnosc komend krytycznych |
| Critical Command ACK Timeout (ms) | `criticalCommandAckTimeoutMs` | 3000 | 500-15000 | Kontrola timeoutow ACK |
| Incident Message Language | `operatorUiLanguage` | `pl` | `pl`/`en` | Ujednolicenie komunikatow operatora i raportow incydentow |
| Timeline Quick Notes Templates | `timelineQuickNoteTemplates` | lista domyslna | lista string | Szybkie adnotacje terapeuty |

## Macierz awarii (AS-IS vs TO-BE)
Ocena bazowa:
- krotkie awarie: `7/10`
- dlugie przerwy i odzyskiwanie: `4/10`
- kompletnosc danych badawczych: `5/10`

| Scenariusz | Odpornosc dzis | Co dopiac |
|---|---|---|
| Krotka utrata polaczenia mobilka -> Unity | Dobra: reconnect loop, retry ACK, attach flow | Twardy licznik prob + telemetryczny `recoveryOutcome` |
| Krotka utrata polaczenia Unity -> mobilka | Dobra/srednia: watchdog + reconnect | Stan `unity_alive_but_frozen` jako osobny sygnal |
| Wielokrotne resetowanie Wi-Fi w trakcie 1h | Srednia | Formalny `sessionRecoveryWindowMinutes` + automat decyzyjny |
| END_SESSION przy niespelnionym attach | Dobra po hotfixie | E2E testy integracyjne tego przypadku |
| Falszywy popup handoff podczas aktywnej gry | Lepsza niz wczesniej | `Deferred badge` zamiast popupu |
| Mobilka ubita/restartowana | Srednia | Auto-resume kontekstu po loginie (`therapistId + studentId`) |
| Unity ubite/restartowane podczas sesji | Srednia/slaba | Trwaly journal lokalny + replay po starcie |
| Dziecko konczy gre offline i zamyka app | Slaba/srednia | `Durable event log` niezalezny od sieci + pozniejszy flush |
| Brak reconnect przez > okno recovery | Brak formalnej polityki | Decyzja terapeuty: `Resume / End / Keep interrupted` |
| Powrot po > okno recovery (ten sam terapeuta + dziecko) | Czesciowo | Gate: `therapistId + studentId + unfinished + age` |
| Duplikaty komend/eventow po reconnect | Czesciowo | Idempotencja po `messageId/eventId` i deduplikacja zapisu |
| Niezgodnosc czasu urzadzen | Ryzyko analityczne | Monotoniczny `sequenceNumber` obok timestampu |
| Zawieszenie warstwy przy zyjacym TCP | Czesciowo | Heartbeat per warstwa: transport/runtime/gameplay/telemetry |

Najwieksza luka:
- brak formalnej polityki okna recovery jako state machine,
- brak gwarancji trwalego buforowania calosci eventow gameplayowych przy ubiciu Unity.

## Docelowe stany sesji
1. `ACTIVE`
2. `INTERRUPTED_RECOVERING_UNDER_WINDOW`
3. `INTERRUPTED_OVER_WINDOW_NEEDS_THERAPIST_DECISION`
4. `RESUMED`
5. `CLOSED_BY_THERAPIST`
6. `CLOSED_BY_COMPLETION_PENDING_SYNC`
7. `ARCHIVED_SYNCED`

## Reguly odzyskiwania i zamykania
1. Jezeli `now - lastConnectionLostAt <= sessionRecoveryWindowMinutes`, system robi cichy reconnect/attach bez popupu.
2. Jezeli `now - lastConnectionLostAt > sessionRecoveryWindowMinutes`, mobilka pyta terapeute: `Resume / End / Keep interrupted`.
3. Jezeli `now - interruptedAt > interruptedSessionAutoCloseHours` i `autoCloseInterruptedSessionsEnabled=true`, mobilka auto-zamyka sesje i zapisuje audit event.
4. Unity nie decyduje o wlascicielu sesji, tylko odtwarza kontekst przekazany przez mobilke.

## Dane pod badania i ML (kanoniczny event schema)
Cel: nie wiecej danych, tylko dane spojne, porownywalne i wiarygodne.

Wymagania bazowe:
1. Jednolity event schema dla wszystkich gier.
2. Stabilne identyfikatory: `sessionId`, `taskRunId`, `attemptId`, `eventId`, `sequenceNumber`.
3. Metadane: terapeuta (pseudonim), dziecko (pseudonim), gra, poziom, konfiguracja.
4. Dane urzadzenia: model headsetu, kontrolery, FPS, latency, tracking quality.
5. Interakcje atomowe: hover/select/grab/release/poke/ray_hit/button_press.
6. Reka i zrodlo wejscia: left/right, controller/hand, trigger/grip/thumbstick/button + wartosc analogowa.
7. Intencja i wynik: required/correct/incorrect/late/redundant/omitted.
8. Czas reakcji: cue -> first action, cue -> correct action, inter-action interval.
9. Trajektorie probkowane: pozycja i orientacja glowy/rak w probkach (nie full stream stale).
10. Audio/uwaga: zrodlo bodzca, poprawne wskazanie, czas decyzji.
11. Wynik zadania: sukces, liczba bledow, podpowiedzi, restarty, fatigue signals.
12. Adnotacje terapeuty jako rownoprawne eventy timeline.
13. Jakosc danych: reconnect, retry, local replay, missing packets, confidence score.
14. Zgody/retencja/prywatnosc zgodne z wymaganiami klinicznymi.
15. Pipeline etykiet pod ML per `taskRunId`.

## Interakcje narzedziowe (wymaganie core)
Obowiazkowo telemetryzujemy takze akcje typu narzedzie -> cel:
1. Podniesienie narzedzia.
2. Trzymanie narzedzia (czas, reka, stabilnosc).
3. Uzycie narzedzia.
4. Trafienie i jakosc trafienia (punkt, kat, sila, timing).
5. Poprawnosc celu i wynik semantyczny.

## Proponowane core komponenty
| Komponent | Zakres |
|---|---|
| `SessionRecoveryManager` | FSM sesji, okna recovery, decyzje terapeuty |
| `DurableEventOutbox` | Trwaly bufor eventow i wynikow po stronie Unity |
| `CommandJournal` | Journal komend krytycznych i ich statusow |
| `IdempotencyGuard` | Deduplikacja komend/eventow po `messageId/eventId` |
| `InteractionEventBridge` | Adapter Meta Interaction SDK -> kanoniczny event schema |
| `HandContextResolver` | Kontekst lewej/prawej reki i zrodla input |
| `PointerProbe` | Ray start/hover/hit/select i target validity |
| `ToolGripTracker` | Chwyt narzedzia, reka, czas trzymania |
| `ToolUseIntentTracker` | Czy akcja narzedziem byla wymagana przez task |
| `ToolImpactProbe` | Parametry trafienia narzedziem |
| `TargetValidationZone` | Walidacja poprawnego celu |
| `ActionOutcomeClassifier` | correct/incorrect/late/redundant/omitted |
| `SequenceTaskEngine` | Wspolny silnik zadan sekwencyjnych |
| `StimulusScheduler` | Harmonogram bodzcow audio/wizualnych |
| `TaskOutcomeAggregator` | Ujednolicony wynik tasku i sesji |
| `TherapistTimelinePanel` | Timeline systemowy + notatki + quick actions |
| `AdaptiveDifficultyController` | Adaptacja trudnosci i przygotowanie pod AI |

Mapowanie do typow gier:
- Pinata: `ToolGripTracker`, `ToolImpactProbe`, `TargetValidationZone`, `TaskOutcomeAggregator`.
- Morse/kokosy/dzwony: `SequenceTaskEngine`, `StimulusScheduler`, `TaskOutcomeAggregator`.
- Drzewa audio: `StimulusScheduler`, `PointerProbe`, `TaskOutcomeAggregator`.
- Motyle: `ToolGripTracker`, `TargetValidationZone`, `ActionOutcomeClassifier`.
- Puzzle: `ToolGripTracker`, `TargetValidationZone`, `TaskOutcomeAggregator`.

## Roadmap wdrozenia
### P0 (krytyczne)
Zakres:
1. Session ownership rules i blokady (`therapistId + studentId + sessionId`).
2. `SessionRecoveryManager` z konfigurowalnym oknem recovery.
3. Auto-close interrupted sessions + audit event.
4. `DurableEventOutbox` + replay po restarcie Unity.
5. `CommandJournal` + `IdempotencyGuard`.
6. Integracyjne E2E dla `END_SESSION` i reconnect.

Kryteria wyjscia:
1. Brak ukrytych duplikatow sesji.
2. Brak utraty eventow po kill/restart Unity.
3. `END_SESSION` deterministycznie dziala lub zwraca jawny powod bledu.

### P1 (operacyjny UX terapeuty)
Zakres:
1. Gear settings na ekranie Students.
2. `TherapistTimelinePanel` (system + notatki + quick templates).
3. `Deferred badge` dla odlozonego handoff.
4. Auto-resume kontekstu po ponownym loginie terapeuty.

Kryteria wyjscia:
1. Terapeuta widzi co sie stalo i co moze bezpiecznie zrobic.
2. Wszystkie decyzje sesji sa odtworzalne z timeline.

### P2 (telemetria gameplay i badania)
Zakres:
1. `InteractionEventBridge` we wszystkich aktywnych grach.
2. Komponenty narzedziowe (`ToolGripTracker`, `ToolImpactProbe`, `TargetValidationZone`).
3. `SequenceTaskEngine`, `StimulusScheduler`, `TaskOutcomeAggregator`.
4. Raport jakosci danych po reconnect/offline.

Kryteria wyjscia:
1. Spójna semantyka eventow miedzy grami.
2. Potwierdzona kompletnosc danych po scenariuszach awarii.

### P3 (AI readiness)
Zakres:
1. `AdaptiveDifficultyController`.
2. Label pipeline per task run.
3. Walidacja dataset quality przed treningiem.

Kryteria wyjscia:
1. Dane gotowe do bezpiecznego modelowania i analiz klinicznych.

### OPS (operacyjny lane po roadmapie)
Zakres:
1. Eksport datasetu z kanonicznych eventow do artefaktow pre-train.
2. Manifest pre-train z jakością danych i pokryciem task run.
3. Sanity checks eksportu (`taskRunId`/`label`/`summary`, `sourceOfTruth`, `ownerKey`/`sessionKey`).
4. Gotowy raport operatorski do decyzji rollout/training.
5. Podpiecie lane do realnych sladow sesji (`DurableEventOutbox` NDJSON: `interaction_event` + `payloadJson`).
6. SOP operatorski i paczka handoff pre-train (manifest handoff + checksums + checklist).
7. Jawny flow potwierdzenia intake z trail ACK (`intakeAck` + `ackTrail`) w `handoff_manifest.json`.
8. Auto-discovery trace intake (`ops_dataset_trace_collect.ps1`) dla Quest/ADB i lokalnych sciezek.

Kryteria wyjscia:
1. Artefakty (`canonical_events.ndjson`, `pretrain_manifest.json`, `operator_report.md`) sa generowane automatycznie.
2. Walidacja CLI zatrzymuje lane przy niespelnionych sanity checks.
3. Walidacja CLI obsluguje wejscie realnego trace sesji (z opcjonalnym filtrem `sessionId`).
4. Paczka handoff jest generowana jednym skryptem operatorskim i zawiera integralnosc plikow (`sha256`).
5. Potwierdzenie intake zostawia maszynowo czytelne slady ACK i ticket intake.
6. Operator moze automatycznie pozyskac i przygotowac trace input (Quest lub lokalny path) bez recznego szukania plikow.

## Plan wykonania P0 (RDM-001..RDM-006)
| Kolejnosc | ID | Zakres wykonawczy | Zaleznosci | Status |
|---|---|---|---|---|
| 1 | RDM-001 | Wymuszenie ownership (`ownerKey`, `sessionKey`) w krytycznych komendach i sygnalach oraz blokady przejecia obcej sesji | brak | DONE |
| 2 | RDM-002 | `SessionRecoveryManager` z `sessionRecoveryWindowMinutes` i formalnymi przejsciami `UNDER_WINDOW`/`OVER_WINDOW` | RDM-001 | DONE |
| 3 | RDM-003 | Auto-close `INTERRUPTED` po `interruptedSessionAutoCloseHours` + wpis audytowy | RDM-002 | DONE |
| 4 | RDM-004 | `DurableEventOutbox` i replay po restarcie Unity | RDM-001 | DONE |
| 5 | RDM-005 | `CommandJournal` + `IdempotencyGuard` dla komend/eventow | RDM-001, RDM-004 | DONE |
| 6 | RDM-006 | E2E: reconnect/kill/restart + deterministyczne `END_SESSION` i reason codes | RDM-001..RDM-005 | DONE |

## Co sprawdzac i poprawiac w kazdym cyklu
1. Czy reguly ownership sesji sa zachowane dla wszystkich sciezek reconnect i login.
2. Czy eventy lokalne przechodza flush po reconnect i po restarcie Unity.
3. Czy `sequenceNumber` jest monotoniczny i bez luk logicznych.
4. Czy timeline pokazuje pelna historie decyzji i awarii.
5. Czy nie pojawily sie nowe popupy handoff poza kontekstem entry/reconnect.
6. Czy E2E testy regresyjne przechodza.
7. Czy logi decyzji zawieraja jasne reason codes.

## Board postepu
Status legend:
- `TODO`
- `IN_PROGRESS`
- `DONE`
- `BLOCKED`

| ID | Obszar | Zadanie | Priorytet | Status | Owner | Evidence |
|---|---|---|---|---|---|---|
| RDM-001 | Ownership | Session ownership rules i blokady przejecia | P0 | DONE | Team | `docs/evidence/20260222_000000/notes/SUMMARY.md` |
| RDM-002 | Recovery | Konfigurowalne okno recovery | P0 | DONE | Team | `docs/evidence/20260222_000820/notes/SUMMARY.md` |
| RDM-003 | Recovery | Auto-close interrupted sessions + audit | P0 | DONE | Team | `docs/evidence/20260222_002306/notes/SUMMARY.md` |
| RDM-004 | Durability | Durable outbox + replay po restarcie Unity | P0 | DONE | Team | `docs/evidence/20260222_120301/notes/SUMMARY.md` |
| RDM-005 | Idempotency | Command journal + deduplikacja | P0 | DONE | Team | `docs/evidence/20260222_131144/notes/SUMMARY.md` |
| RDM-006 | Tests | E2E scenariusze awarii i END_SESSION | P0 | DONE | Team | `docs/evidence/20260222_131144/notes/SUMMARY.md`; `docs/evidence/20260222_143237/notes/SUMMARY.md` |
| RDM-007 | UX | Gear settings w Students | P1 | DONE | Team | `docs/evidence/20260222_144153/notes/SUMMARY.md` |
| RDM-008 | UX | Timeline system + notes + templates | P1 | DONE | Team | `docs/evidence/20260222_145657/notes/SUMMARY.md` |
| RDM-009 | UX | Deferred handoff badge | P1 | DONE | Team | `docs/evidence/20260222_150453/notes/SUMMARY.md` |
| RDM-010 | Telemetry | Interaction bridge i event schema we wszystkich grach | P2 | DONE | Team | `docs/evidence/20260222_154216/notes/SUMMARY.md` |
| RDM-011 | Telemetry | Tool telemetry stack | P2 | DONE | Team | `docs/evidence/20260222_165305/notes/SUMMARY.md` |
| RDM-012 | Telemetry | Sequence/stimulus/task outcome stack | P2 | DONE | Team | `docs/evidence/20260222_171605/notes/SUMMARY.md` |
| RDM-013 | AI | Adaptive difficulty + label pipeline | P3 | DONE | Team | `docs/evidence/20260222_174008/notes/SUMMARY.md` |
| RDM-014 | AI | Dataset quality validation before training | P3 | DONE | Team | `docs/evidence/20260222_183204/notes/SUMMARY.md` |
| OPS-001 | Ops | Operacyjny lane pod trening i rollout (`dataset export` + `pre-train manifest` + sanity checks + raport operatora) | P4 | DONE | Team | `docs/evidence/20260222_185857/notes/SUMMARY.md` |
| OPS-002 | Ops | Integracja lane eksportu z realnymi trace sesji + SOP/operator handoff paczki pre-train | P4 | DONE | Team | `docs/evidence/20260222_192103/notes/SUMMARY.md` |
| OPS-003 | Ops | Real-trace-required run + operator intake ACK trail (handoff confirmation flow) | P4 | DONE | Team | `docs/evidence/20260222_202038/notes/SUMMARY.md`; `docs/evidence/20260222_221123/notes/SUMMARY.md`; `docs/evidence/20260223_101542/notes/SUMMARY.md`; `docs/evidence/20260223_171046/notes/SUMMARY.md` |
| MVP-001 | Mobile | Parent MVP: guided start + podglad + progress snapshot + rewards unlock bridge | P4 | DONE | Team | `docs/evidence/20260223_105833/notes/SUMMARY.md` |
| OPS-004 | Ops | Stala bramka E2E (`Unity+Flutter+Firebase`) + orchestrator OPS-003 run (`ops003_real_trace_ready_gate.ps1`) | P4 | DONE | Team | `docs/evidence/20260223_105833/notes/SUMMARY.md`; `docs/evidence/20260223_135526/notes/SUMMARY.md` |

## Zasady aktualizacji roadmapy
1. Kazda istotna zmiana architektury lub polityki sesji aktualizuje ten dokument.
2. Kazdy zamkniety item boardu musi miec link do evidence.
3. Wpisy wykonawcze ida rownolegle do `docs/08-Session-Resilience-Worklog.md`.
