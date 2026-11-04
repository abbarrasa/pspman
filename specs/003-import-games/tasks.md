# Game Catalog CSV Import Tasks

Source: [specification](./spec.md), [implementation plan](./plan.md), and the revised [export specification](../002-export-games/spec.md). Tasks are dependency ordered and estimated at 20–30 minutes or less each. For every implementation task, write its focused tests first, then the production change; leave the solution building and relevant tests passing. Preserve pre-existing Git changes and follow `AGENTS.md` before branching. The no-code instruction remains in force: this checklist does not authorize implementation. Complete one task at a time and stop for review.

## Align export and prepare the shared CSV format

- [x] **T01 (25 min; FR-2, FR-3):** Introduce the shared seven-heading CSV schema and change export to write the seven columns in the specified order without approximate size.
  Done when: Exact-byte tests verify the BOM, seven headings, row order, SizeBytes, and header-only empty export; the solution builds.
- [x] **T02 (25 min; FR-2, FR-3):** Export Medium as `Dvd`/`Usb`/`MemoryCard` and Format as `ISO`/`CHD`, preserving CSV escaping and formula protection; reconcile the old export plan/task acceptance text with the revised specification.
  Done when: Export regression tests cover every label and special text, the old eight-column expectations are gone, and the relevant tests pass.
- [x] **T03 (25 min; FR-13):** Generate the illustrative CSV template from the shared schema, showing every permitted Medium and Format value with other required game fields empty.
  Done when: Exact-byte tests verify its seven headings and allowed labels, and parsing its rows cannot produce a valid game record.

## Parse and validate CSV without file access in Core

- [x] **T04 (25 min; FR-2, FR-3):** Add an incremental tokenizer for semicolon-separated logical records and source line tracking.
  Done when: Unit tests recover ordinary cells and record start lines from chunked CRLF/LF input without loading the complete file.
- [x] **T05 (25 min; FR-2, FR-3):** Extend the tokenizer for quoted semicolons, doubled quotes, Unicode, and line breaks inside quoted Notes.
  Done when: Tests show each multiline record remains one logical row with its exact decoded cell values.
- [x] **T06 (25 min; FR-6, FR-9, FR-11):** Distinguish blank or spaces/tabs-only lines outside quotes from malformed quoting and unterminated records.
  Done when: Tests ignore those blank lines, preserve whitespace inside quotes, and report unrecoverable CSV structure as a fatal preview error.
- [x] **T07 (25 min; FR-2, FR-9):** Map headings by case-insensitive name, reject whitespace in any heading, require all seven fields, and select the last duplicate heading.
  Done when: Tests cover reordered, repeated, extra, missing, mixed-case, and whitespace-containing headings with deterministic column indices or file errors.
- [x] **T08 (25 min; FR-3, FR-6):** Convert a row's Medium, Format, DvdNumber, and SizeBytes using the permitted labels and invariant numeric rules.
  Done when: Tests accept mixed-case values without whitespace and valid integer boundaries, and reject whitespace, unsupported labels, overflow, and malformed numbers by field.
- [x] **T09 (25 min; FR-3, FR-6):** Map the remaining text fields, remove exactly one formula-protection tab when required, and reuse `GameRecordRules` for record validation.
  Done when: Tests cover required Title/Location, path syntax, DVD-only numbering, Notes length, punctuation, and preserved or removed leading tabs.
- [x] **T10 (20 min; FR-6):** Reject rows shorter than the header, ignore cells beyond it, and attach the starting source line and reason to each row error.
  Done when: A short row yields one identified error, a surplus-cell row keeps its first header-width cells, and later valid rows remain usable.

## Plan changes before writing

- [x] **T11 (25 min; FR-4, FR-5):** Build case-sensitive game identity keys and keep only the last valid CSV row per identity.
  Done when: Tests show identical Medium/Location/DVD keys collapse to the last valid row, invalid later rows do not replace it, and differently cased Locations stay distinct.
- [x] **T12 (25 min; FR-4, FR-7):** Classify final rows against one catalog snapshot as new, modified, or no-op; compare absent and empty Notes as equivalent.
  Done when: Tests count each final identity once, omit unchanged matches from all three preview counts, and preserve existing IDs for proposed edits.
- [x] **T13 (25 min; FR-2, FR-3, FR-9):** Add the Core import-source contract and Infraestructure local-file reader with strict UTF-8 and optional BOM support.
  Done when: Disposable-file tests read a normal export incrementally and reject invalid UTF-8 or an unreadable source without catalog writes.
- [x] **T14 (25 min; FR-9, FR-12):** Add source content fingerprints and a read-handle/change-check seam to the file adapter.
  Done when: Tests distinguish unchanged content from same-length or same-timestamp modifications and can inject a read failure at a chosen point.
- [x] **T15 (30 min; FR-2, FR-4–FR-7, FR-9):** Implement preview orchestration: load the catalog once, stream and validate the CSV, retain final candidates and row errors, and compute summary counts.
  Done when: Service tests preview new, edited, duplicate, invalid, and no-op rows without calling any repository write method.
- [x] **T16 (25 min; FR-7, FR-9, FR-11):** Handle preview cancellation, header-only input, fatal parse/read errors, and corrupt catalog reads.
  Done when: Tests show zero proposed changes for header-only input, no writes after cancellation or fatal failure, and a distinct corruption result.

## Apply confirmed changes safely

- [x] **T17 (25 min; FR-7, FR-9, FR-12):** Reopen and verify the complete source fingerprint at confirmation before the first catalog write.
  Done when: Changed, missing, unreadable, or unverifiable files stop before any save, while an unchanged file proceeds.
- [x] **T18 (30 min; FR-4, FR-5, FR-7, FR-8, FR-10):** Apply selected final candidates through the repository, creating new IDs, preserving existing IDs on edits, and skipping no-ops.
  Done when: Tests prove create/edit effects, empty-Notes updates, no update call for an identical match, last-valid behavior, and no game-file access.
- [ ] **T19 (25 min; FR-8, FR-9):** Report ordinary per-record save failures and continue; stop further writes on catalog corruption.
  Done when: Fault-injected tests show accurate created/updated/failed counts, successful earlier writes retained, later valid rows attempted after ordinary failure, and no later writes after corruption.
- [ ] **T20 (30 min; FR-8, FR-9, FR-12):** Re-read and compare selected source rows while applying, then verify the source before reporting complete success.
  Done when: Deterministic changed-file and mid-import read-failure tests stop further saves, report the error and partial counts, and retain records already saved.

## Add Desktop actions and presentation

- [ ] **T21 (20 min; FR-1, FR-9):** Add the Desktop single-file picker abstraction and Avalonia adapter for a local CSV.
  Done when: Tests distinguish a chosen local path, user cancellation, and an unusable or nonlocal selection.
- [ ] **T22 (25 min; FR-1, FR-7, FR-9):** Add import state and commands to the main view model, and wire source/picker dependencies at startup.
  Done when: View-model/startup tests can open import, choose or cancel a CSV, and block import when the catalog is unavailable without changing it.
- [ ] **T23 (25 min; FR-6, FR-7, FR-9, FR-11):** Run preview away from the UI thread and expose English counts, row errors, busy state, and fatal-error feedback.
  Done when: Tests observe accurate new/modified/error counts, header-only preview, and visible failure without catalog writes.
- [ ] **T24 (25 min; FR-4, FR-7, FR-8, FR-12):** Add preview confirmation/cancellation and final results to the view model; block competing add/edit/delete actions while preview or apply is active.
  Done when: Tests show cancellation leaves the catalog intact, confirmation displays only confirmed writes, changed-source errors remain visible, and competing commands are disabled.
- [ ] **T25 (25 min; FR-13):** Save the template through the existing protected export writer and destination/overwrite flow without reading the catalog.
  Done when: Tests produce the seven-column template, decline replacement without changing an existing file, and leave catalog bytes unchanged.
- [ ] **T26 (30 min; FR-1, FR-7–FR-9, FR-11, FR-12):** Add typed-bound Import controls, preview summary, row-error list, confirmation/cancellation, progress, and final counts to the main window.
  Done when: Headless tests operate the visible controls, verify English feedback, and see the refreshed list only after confirmed writes.
- [ ] **T27 (20 min; FR-13):** Add the typed-bound Get CSV template action and destination/replacement feedback to the main window.
  Done when: A headless test saves a template through the UI and verifies its content without a catalog change.

## End-to-end acceptance and documentation

- [ ] **T28 (25 min; FR-2, FR-3, FR-4, FR-7, FR-8, FR-10):** Add a disposable SQLite-backed export/import round-trip test for multiline Notes, formula-like text, offline media, and otherwise unchanged records.
  Done when: A new export imports without creations, updates, or errors; IDs, catalog bytes, and game files remain unchanged.
- [ ] **T29 (25 min; FR-4, FR-5, FR-7, FR-8):** Add a SQLite-backed functional test that edits one exported row, repeats an identity, and leaves another row unchanged.
  Done when: Only the last valid changed row is saved and counted, its ID is preserved, and the unchanged row is neither counted nor written.
- [ ] **T30 (25 min; FR-2, FR-6, FR-7, FR-11):** Add a headless preview test for reordered/extra/repeated headings and short, invalid, and whitespace-only rows.
  Done when: New/modified/error counts and starting source lines match the expected logical rows, while valid later rows remain eligible.
- [ ] **T31 (20 min; FR-11, FR-13):** Add a headless template-flow test including import of the unedited template and a header-only CSV.
  Done when: Template values are visible, neither file creates a game, and header-only input reports zero proposed changes.
- [ ] **T32 (30 min; FR-1, FR-7–FR-9, FR-12):** Add headless cancellation and source-failure tests with disposable catalog and CSV files.
  Done when: Picker/preview cancellation and pre-save changes leave catalog bytes untouched; a read failure after import begins reports partial results and retains earlier successful writes.
- [ ] **T33 (25 min; FR-1–FR-13):** Build and test the solution, measure line and branch coverage, and write the FR-to-test coverage report with justified gaps.
  Done when: Build and tests pass, measured coverage is recorded, and every FR maps to automated evidence.
- [ ] **T34 (30 min; FR-1–FR-13):** Perform and record the graphical import/template walkthrough, check Excel compatibility where available, and replace stale import/export notes in project memory.
  Done when: Manual results record each required flow and any unavailable Excel check as pending, and memory describes the implemented state rather than this draft plan.
