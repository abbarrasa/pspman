# Game Catalog CSV Export Tasks

Source: [specification](./spec.md) and [implementation plan](./plan.md). Tasks are dependency ordered, estimated at no more than 20–30 minutes each, and must leave the solution building and relevant tests passing. Write focused tests before implementation. Do not begin implementation until the specification and plan are approved and the no-code instruction is lifted.

## Shared formatting and CSV rules

- [x] **T01 (20 min; FR-3):** Move the existing binary-size formatter to Core, use it from the game editor, and pin its current output with unit tests.
  Done when: The editor still displays the same binary-unit values, formatter tests pass, and the solution builds.
- [x] **T02 (25 min; FR-2, FR-3, FR-9):** Add a pure CSV serializer for the fixed English header, UTF-8 BOM, semicolon separator, and header-only empty catalog; test exact bytes.
  Done when: Unit tests verify the BOM, eight headers in order, and a header-only export for zero records.
- [x] **T03 (25 min; FR-2, FR-3):** Project each saved record into the eight export columns, preserving input order and excluding the internal ID; test DVD and non-DVD values.
  Done when: Tests verify every visible field, invariant integer bytes, the shared readable size, empty non-DVD DVD cells, and one ordered row per record.
- [x] **T04 (25 min; FR-3, FR-4):** Add CSV field escaping and tests for semicolons, quotes, commas, colons, Unicode, line breaks, and empty Notes.
  Done when: Tests parse the output into the original cell values, with embedded quotes doubled and no extra logical rows.
- [x] **T05 (25 min; FR-4, FR-5):** Protect formula-like text in exported cells, including triggers after leading whitespace; test protected and ordinary values.
  Done when: Tests cover `=`, `+`, `-`, and `@`, verify the planned protective tab and quoting, and prove the catalog values remain unchanged.

## Destination and safe publication

- [x] **T06 (20 min; FR-1, FR-7, FR-8):** Add pure export filename validation for one nonempty `.csv` name and safe resolution within the chosen folder.
  Done when: Tests reject separators, traversal, unusable names, and the catalog destination while accepting a normal CSV filename.
- [x] **T07 (25 min; FR-7, FR-8):** Define the Core export-writer contract and implement staged publication for a new destination in Infraestructure.
  Done when: An integration test writes complete bytes to a new file through the contract, with no staging file left behind.
- [x] **T08 (25 min; FR-8):** Handle failed staging writes and publication errors, cleaning owned temporary files and preserving any existing destination.
  Done when: Deterministic failure tests show no partial new export, unchanged existing bytes, and a reported error.
- [x] **T09 (25 min; FR-6, FR-8):** Require explicit overwrite consent in the writer and replace an approved existing file only after staging completes.
  Done when: Tests show refusal without consent, successful approved replacement, and unchanged original bytes when replacement fails.
- [x] **T10 (20 min; FR-6, FR-8):** Recheck destination state before publication so a file created or changed after consent is not silently overwritten.
  Done when: A deterministic race test reports the conflict, preserves the other file, and removes the owned staging file.

## Desktop export flow

- [x] **T11 (20 min; FR-1, FR-8):** Add a Desktop folder-picker abstraction and Avalonia adapter that return a local folder path or cancellation without creating files.
  Done when: Tests cover local selection, cancellation, and an unusable nonlocal result; the solution builds.
- [x] **T12 (25 min; FR-1, FR-8, FR-9):** Add export form state and commands to the main view model for opening, choosing a folder, naming the file, and cancelling; block export when the catalog is unavailable or corrupt.
  Done when: View-model tests prove cancellation changes no file, empty catalogs may proceed, and unavailable catalogs cannot export.
- [x] **T13 (30 min; FR-2, FR-7, FR-9):** Coordinate a fresh catalog read, serialization, and new-file publication from the view model, with success reported only after the writer succeeds.
  Done when: Tests export every saved row despite selection or disconnected media, export a header-only empty catalog, and report the saved path after success.
- [x] **T14 (25 min; FR-6):** Add pending replacement confirmation in the view model and route only an approved choice to the writer.
  Done when: Tests show an existing destination prompts for confirmation and decline leaves it byte-for-byte unchanged without success feedback.
- [x] **T15 (25 min; FR-1, FR-8):** Surface invalid destination, catalog read/corruption, and writer failures in the view model without a false success state.
  Done when: Tests distinguish these failures, leave destination bytes unchanged, and preserve the catalog's existing corruption behavior.
- [ ] **T16 (20 min; FR-1, FR-7, FR-8):** Compose the picker and writer at Desktop startup while preserving the temporary-root test seam.
  Done when: A startup test resolves the export dependencies against a disposable root and existing catalog startup tests pass.
- [ ] **T17 (25 min; FR-1, FR-2, FR-9):** Add a typed-bound Export catalog action, folder choice, and filename form to the main window.
  Done when: Headless UI tests can open the form, enter a name, cancel it, and start an empty-catalog export without a selected game.
- [ ] **T18 (25 min; FR-6, FR-7, FR-8):** Add typed-bound replacement confirmation and English success/error feedback to the main window.
  Done when: Headless UI tests verify visible Replace/Cancel choices, an unchanged file on decline, a success path only after publication, and visible failure text.

## End-to-end acceptance

- [ ] **T19 (30 min; FR-2, FR-3, FR-7, FR-9):** Add SQLite-backed functional tests for full saved-order export and a header-only empty export, including offline records and no selected game.
  Done when: Tests inspect the exported bytes and prove the saved catalog and game files are unchanged.
- [ ] **T20 (25 min; FR-3, FR-4, FR-5):** Add a functional test that exports punctuation, quoted multiline Notes, Unicode, and formula-like text to a real CSV file.
  Done when: The file retains each value in one logical cell and contains the planned Excel text protection without changing catalog values.
- [ ] **T21 (30 min; FR-1, FR-6, FR-8):** Add functional cancellation and failure tests using disposable destinations and a corrupt catalog.
  Done when: Tests cover cancelled selection, declined replacement, corrupt reads, and failed writes without false success or changed existing bytes.
- [ ] **T22 (25 min; FR-1–FR-9):** Run the full solution build and tests, measure line and branch coverage, and record FR-to-test evidence and justified gaps in the coverage report.
  Done when: The build and tests pass, coverage numbers are recorded, and every FR has linked automated evidence.
- [ ] **T23 (30 min; FR-1–FR-9):** Perform the manual Spanish-Excel walkthrough, record observed results, and update project memory to the implemented state.
  Done when: The validation record checks nonempty and empty files, special Notes, literal formula-like text, cancellation and replacement, and memory reflects the verified behavior; any unavailable Excel check remains explicitly pending.
