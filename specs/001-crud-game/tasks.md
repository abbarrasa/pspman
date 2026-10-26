# Game Record CRUD Tasks

Source: [specification](./spec.md) and [implementation plan](./plan.md). Tasks are dependency ordered; each is estimated at 20–30 minutes maximum. Keep the solution building and relevant tests passing after every task. Execute no implementation task until the specification and plan are approved and the no-code instruction is lifted.

## Core rules and use cases

- [x] **T01 (20 min; FR-2, FR-3):** Add record/input types, stable ID, and the allowed Medium and Format values in Core; test their value semantics.
  Done when: Core tests prove all fields and allowed values can be represented, and the solution builds.
- [x] **T02 (25 min; FR-2, FR-4):** Add pure required-field, Unicode-whitespace Title, Medium, and Format validation with field-specific results and boundary tests.
  Done when: Tests reject every missing or unsupported value and accept valid choices without persistence.
- [x] **T03 (25 min; FR-2, FR-4):** Add DVD Number normalization and validation, positive byte Size validation, and the 2,500-character Notes limit with boundary tests.
  Done when: Tests cover DVD null/zero/negative/positive, non-DVD number rejection, Size boundaries, and Notes at 2,500/2,501 characters.
- [x] **T04 (25 min; FR-3, FR-4, FR-9):** Add pure medium-rooted Location validation without checking the host file system; test valid and malformed paths.
  Done when: Tests accept a valid `/`-rooted file path while rejecting missing filename, empty/dot/traversal segments, control characters, and host-dependent validation.
- [ ] **T05 (20 min; FR-6):** Add the pure duplicate-identity comparison and tests for exact Location case, Medium, and DVD Number.
  Done when: Tests prove Title and Format do not affect identity, case-different paths stay distinct, and DVD numbers distinguish DVD records.
- [ ] **T06 (25 min; FR-1, FR-3, FR-10):** Add the Core repository contract and service list/read operations with an in-memory fake and failure tests.
  Done when: Tests return saved records or an empty catalog, expose record details, and report read failures without inventing data.
- [ ] **T07 (30 min; FR-4, FR-5, FR-6, FR-9, FR-10):** Add service creation using the rules and repository contract; test invalid, duplicate, offline, success, and failed-save paths.
  Done when: Tests show only valid unique records persist, no media check occurs, and a repository failure is reported as unsaved.
- [ ] **T08 (25 min; FR-4, FR-6, FR-7, FR-9, FR-10):** Add service editing, including self-exclusion from duplicate detection, and focused fake-repository tests.
  Done when: Tests prove valid edits replace only the selected record, duplicates/invalid input leave it intact, and failed updates are reported.
- [ ] **T09 (20 min; FR-8, FR-10):** Add the service delete operation for a confirmed catalog-record ID and test success and failure.
  Done when: Tests prove deletion affects only the requested repository record and failed deletion is reported; Core has no file-access dependency.

## SQLite storage

- [ ] **T10 (30 min; FR-1, FR-3, FR-11):** Add Microsoft.Data.Sqlite and the repository's schema creation plus list/read against disposable databases; add the integration test project to the solution.
  Done when: Integration tests open a fresh catalog, read an empty list, and round-trip all record fields without touching a real user catalog.
- [ ] **T11 (25 min; FR-5, FR-6, FR-10):** Add parameterized insert and the two case-sensitive database uniqueness rules; test persistence after reopening and duplicate rejection.
  Done when: Integration tests reject duplicate DVD and non-DVD keys, allow case-different Locations, and preserve existing rows after rejection.
- [ ] **T12 (30 min; FR-6, FR-7, FR-8, FR-10):** Add parameterized atomic update/delete and integration tests for changed values, edited-key conflicts, and record-only deletion.
  Done when: Reopened catalogs show successful edits/deletes, and failed or conflicting writes leave prior rows intact.
- [ ] **T13 (25 min; FR-10, FR-11):** Add storage-failure and corrupt-catalog reporting without automatic repair or overwrite; test with disposable files.
  Done when: Integration tests show read/write failures are surfaced and the bytes of a corrupt existing catalog remain unchanged.

## Desktop flow

- [ ] **T14 (25 min; FR-1, FR-10, FR-11):** Compose the repository and service at startup using a local application-data catalog path; expose startup failures to the main view model.
  Done when: The app builds, starts with a fresh catalog, and a corrupt catalog yields an error without replacing its file.
- [ ] **T15 (25 min; FR-1, FR-3):** Replace the sample greeting with a typed-bound catalog list, selection/details, and empty state.
  Done when: A fresh catalog displays the empty state; saved records can be selected and all saved values are visible.
- [ ] **T16 (30 min; FR-2, FR-3, FR-4):** Add the typed-bound editor fields and permitted choices, with DVD Number shown only for DVD and Size labeled as user-entered decompressed bytes.
  Done when: Every specified field can be entered, Medium/Format choices are constrained, and the UI displays field errors from Core.
- [ ] **T17 (25 min; FR-4, FR-5, FR-6, FR-9, FR-10):** Connect Add/Save to the service and refresh the list only after successful persistence.
  Done when: A valid offline record appears in the list; invalid, duplicate, or failed saves show an error and do not appear as saved.
- [ ] **T18 (25 min; FR-3, FR-4, FR-6, FR-7, FR-9, FR-10):** Load a selected record into the editor and connect Save Changes to the service.
  Done when: Valid changes appear in list/details; invalid, duplicate, or failed changes leave the saved record visible and unchanged.
- [ ] **T19 (25 min; FR-8, FR-10):** Add inline Delete, Confirm, and Cancel states with no game-file operation.
  Done when: Cancel leaves the record; Confirm removes only its catalog row; a failed delete keeps it visible and displays an error.
- [ ] **T20 (25 min; FR-10, FR-11):** Finish visible operation/startup errors and disable catalog mutations after corrupt-data detection.
  Done when: The user sees the failure, no unsuccessful change is presented as saved, and a corrupt catalog cannot be overwritten through the UI.

## Automated acceptance and completion

- [ ] **T21 (30 min; FR-1, FR-2, FR-3, FR-5):** Add the Avalonia.Headless xUnit test project with manual session setup and cover list/empty, create, and record-details controls.
  Done when: Headless tests drive real typed bindings and pass without a graphical display.
- [ ] **T22 (30 min; FR-4, FR-6, FR-7, FR-9, FR-10):** Add headless tests for validation, duplicate feedback, editing, offline media, and failed saves.
  Done when: Each flow shows the expected UI message/state and leaves persisted data unchanged on failure.
- [ ] **T23 (25 min; FR-8, FR-10, FR-11):** Add headless tests for deletion confirmation/cancellation, deletion failure, and corrupt-catalog startup.
  Done when: Tests prove the visible confirmation and error states and protect records when deletion is cancelled or fails.
- [ ] **T24 (30 min; FR-1–FR-11):** Run the full build/tests, produce a line-and-branch coverage report, map every FR to passing tests, and record justified coverage gaps.
  Done when: The solution builds, all tests pass, the report exists, and every FR has unit, integration, or functional evidence as applicable.
- [ ] **T25 (25 min; FR-1–FR-11):** Demonstrate create, list/view, edit, duplicate rejection, confirmed/cancelled deletion, and offline editing on a graphical desktop; update project memory to the implemented state.
  Done when: The manual flow succeeds, any deviation is recorded, and project memory reflects actual behavior rather than the proposed plan.
