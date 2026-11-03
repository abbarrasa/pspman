# Game Catalog CSV Import Implementation Plan

## Scope and sequence

This plan implements [the import specification](./spec.md) and the revised seven-column [export specification](../002-export-games/spec.md). The export serializer currently emits eight columns and `Memory Card`; changing it is a prerequisite for an export/import round trip. No game-file access, database schema change, package, or new service framework is needed. Core owns parsing, validation, identity matching, preview, and import decisions; Infraestructure owns file access and SQLite; Desktop owns pickers, state, and presentation. All code, documentation, and UI text stay in English. [FR-1–FR-13]

Before implementation, inspect `git status --short --branch`, preserve all local changes, and follow the feature-branch rules in `AGENTS.md`. The current worktree already contains documentation edits and an unrelated deletion; do not switch branches if those changes make the move unsafe. Write tests before each implementation slice, then build and test the solution. [FR-1–FR-13]

## Files and responsibilities

| File | Change and responsibility | FR |
| --- | --- | --- |
| `src/PSPMan.Core/Games/GameCatalogCsvSerializer.cs` | Emit exactly the seven agreed headings and fields, with `Dvd`/`Usb`/`MemoryCard` and `ISO`/`CHD`; retain BOM, semicolon quoting, and formula protection. Remove the approximate-size CSV column. | FR-2, FR-3, FR-13 |
| `src/PSPMan.Core/Games/GameCatalogCsvSchema.cs` (new) | Hold the canonical heading order and permitted CSV value labels shared by export, import, and template generation. | FR-2, FR-3, FR-6, FR-13 |
| `src/PSPMan.Core/Games/GameCatalogCsvTokenizer.cs` (new) | Consume decoded characters incrementally and emit logical CSV records with their starting physical line; handle quotes, escaped quotes, delimiters, BOM-free text, and multiline Notes. | FR-2, FR-3, FR-6, FR-9, FR-11 |
| `src/PSPMan.Core/Games/GameCatalogImportRules.cs` (new) | Map headings, decode row values, undo one protective tab, parse invariant numbers and enums, call existing `GameRecordRules`, form case-sensitive identity keys, and identify short or invalid rows. | FR-2–FR-6, FR-9, FR-11 |
| `src/PSPMan.Core/Games/IGameCatalogImportSource.cs` (new) | Define a platform-neutral source contract for streaming the selected CSV and obtaining a content fingerprint; include typed read/change failures. Core does not open paths itself. | FR-1–FR-3, FR-9, FR-12 |
| `src/PSPMan.Core/Games/GameCatalogImportService.cs` (new) | Build immutable preview results, retain only the last valid row per identity, count proposed creates/updates/errors, verify the source at confirmation, save individual records, preserve IDs, and report partial failures. Reuse `GameRecordRules` and the repository contract. | FR-4–FR-12 |
| `src/PSPMan.Core/Games/GameCatalogCsvTemplate.cs` (new) | Produce the seven-column UTF-8 CSV template with all permitted Medium/Format values and blank required game fields. | FR-13 |
| `src/PSPMan.Infraestructure/Games/FileGameCatalogImportSource.cs` (new) | Read local files using strict UTF-8, stream bytes/characters without a fixed file-size limit, calculate a content fingerprint, and detect source changes or read failures. Keep file handles limited to an import operation. | FR-1–FR-3, FR-9, FR-12 |
| `src/PSPMan.Desktop/Services/IGameImportFilePicker.cs` and `AvaloniaGameImportFilePicker.cs` (new) | Choose one local CSV using the existing Avalonia storage-provider pattern; return cancellation distinctly from an unusable local path. | FR-1, FR-9 |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.Import.cs` (new) | Expose import, preview, confirmation, template, progress, row errors, and final-result state and commands; delegate all import rules to Core. | FR-1, FR-7–FR-13 |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.cs`, `src/PSPMan.Desktop/CatalogStartup.cs`, and `src/PSPMan.Desktop/App.axaml.cs` | Wire import dependencies, disable conflicting catalog edits while preview/apply is active, refresh the list after confirmed writes, and surface corruption without presenting failed changes as saved. | FR-1, FR-4, FR-7–FR-10, FR-12, FR-13 |
| `src/PSPMan.Desktop/Views/MainWindow.axaml` | Add typed-bound Import and Template actions, preview summary, confirmation/cancellation, scrollable row errors, progress, and final counts alongside the existing catalog. | FR-1, FR-7–FR-9, FR-11–FR-13 |
| `tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs` | Change exact export expectations to the seven-column format and canonical labels; keep punctuation and formula-safety regression coverage. | FR-2, FR-3, FR-13 |
| `tests/PSPMan.Core.Tests/GameCatalogCsvTokenizerTests.cs`, `GameCatalogImportRulesTests.cs`, `GameCatalogImportServiceTests.cs`, and `GameCatalogCsvTemplateTests.cs` (new) | Unit-test CSV records, heading/row validation, last-valid matching, preview/apply outcomes, and template data. | FR-2–FR-13 |
| `tests/PSPMan.Infraestructure.Tests/FileGameCatalogImportSourceTests.cs` and `SqliteGameCatalogImportTests.cs` (new) | Exercise strict decoding, fingerprints, changed/unreadable files, disposable SQLite create/update/partial failure, ID preservation, and corruption. | FR-3–FR-12 |
| `tests/PSPMan.Desktop.Tests/GameImportFilePickerTests.cs`, `MainViewModelImportTests.cs`, and `GameCatalogImportFlowTests.cs` (new) | Test picker cancellation/error and the complete typed-bound preview, cancel, confirm, template, and result flows with temporary files and a disposable catalog. | FR-1, FR-7–FR-13 |
| `specs/002-export-games/plan.md`, `specs/002-export-games/tasks.md`, and `specs/003-import-games/spec.md` | Reconcile the older export plan/task acceptance text with its revised spec; update the import spec only if implementation exposes an observable gap. Keep completed historical tasks identifiable. | FR-2, FR-3, FR-13 |
| `specs/003-import-games/coverage.md`, `specs/003-import-games/manual-validation.md`, and `MEMORY.md` | Record FR-to-test coverage, measured gaps, manual Excel/UI results, and durable project decisions without a task log. | FR-1–FR-13 |

`AGENTS.md` needs no new standing rule. `MEMORY.md` should replace its current import-planning note with the implemented behavior and any repeatable pitfall, while staying near 50 lines. [FR-1–FR-13]

## Pure logic and algorithms

Pure functions take values and return values or validation errors; they never open a file or call the repository. [FR-2–FR-6, FR-11, FR-13]

- `TryMapHeadings(headings)`: reject whitespace in **any** heading; compare the seven required names with ordinal case-insensitive equality; keep the final index of each repeated heading; ignore other headings; fail if one is absent. [FR-2, FR-9]
- `UndoFormulaProtection(text)`: if the first character is a tab and the first following non-whitespace character is `=`, `+`, `-`, or `@`, remove exactly one initial tab. Otherwise preserve the text. [FR-3]
- `TryMapGameRow(cells, headerMap)`: ignore surplus cells, reject fewer cells than the header, parse `DvdNumber` as an invariant integer (zero becomes null), `SizeBytes` as an invariant positive 64-bit integer, and Medium/Format case-insensitively with no whitespace; then call `GameRecordRules.Validate`. Return field-specific errors. [FR-3, FR-6]
- `Identity(input)`: use Medium, Location with ordinal case-sensitive equality, and normalized DVD number only for DVD. `SelectLastValid` replaces the earlier candidate for that key only after the later row is valid. [FR-4, FR-5]
- `ClassifyCandidates(candidates, catalogSnapshot)`: count distinct new and modified identities, retain existing IDs for edits, and keep invalid-row errors separate from superseded valid rows. A matching row with identical field values is a no-op: compare absent and empty Notes as equivalent, do not count it as new, modified, or erroneous, and do not schedule a write. [FR-4, FR-5, FR-7, FR-8]
- `TemplateRows()`: cover all three Medium and both Format labels with incomplete example rows; no row has the required Title, Location, and SizeBytes. [FR-13]

The tokenizer is a deterministic, incremental state machine rather than a whole-file string operation. It tracks `outside`, `inside-quote`, and `after-quote` states, the current cell, record, physical line, and starting line. A separator ends a cell only outside quotes; doubled quotes become one quote; line breaks inside quotes remain data; a line containing only spaces/tabs outside quotes is ignored; EOF in a quote is a fatal preview error. It yields logical records without loading the whole CSV. [FR-2, FR-3, FR-6, FR-9, FR-11]

Preview pseudocode: [FR-2–FR-7, FR-9, FR-11]

```text
open selected source with strict UTF-8 decoding
read/validate header; load catalog once; abort on corruption
for each logical row, retaining its source line:
    if blank line: continue
    if row invalid: append row error; continue
    candidates[Identity(row)] = row  // last valid wins
finish reading; retain source content fingerprint
classify distinct candidates against catalog snapshot
show counts and row errors; no writes yet
```

Apply pseudocode: [FR-4–FR-9, FR-12]

```text
reopen the same source; verify its complete fingerprint against preview
if changed, unreadable, or unverifiable: fail without writes
keep the source open with no-write sharing where supported
re-read logical rows in order; compare each selected row to preview
for each row selected as the last valid for its identity:
    if source change/read failure detected: stop and report partial counts
    if row matches all values of an existing record: skip without writing or counting it
    if existing ID: update its editable fields; else create a record
    if ordinary save fails: report that row and continue
    if catalog corruption is detected: stop; do not attempt further writes
verify source again before declaring full success
refresh visible catalog; report created, updated, and failed counts
```

The preview retains one candidate per identity plus row errors rather than every data row; memory still grows with the number of distinct games and errors, as allowed by the machine-dependent capacity requirement. The initial whole-file fingerprint prevents writes from a changed file before applying. A held read handle, row comparison, and checks during/after apply detect changes during import; a detected change or read error stops further writes and leaves already saved records in place. Cross-platform file-sharing rules cannot stop every external writer, so report detected changes rather than claiming an absolute OS lock. [FR-7–FR-9, FR-12]

## Interface

Add an **Import catalog** action next to Export. The local-file picker opens the selected CSV; cancelling returns to the catalog unchanged. While previewing, show progress and keep the UI responsive. The preview shows counts for new games, modified games, and erroneous rows, with a scrollable list of source line and English reason. **Import** confirms; **Cancel** discards the preview. Show final created/updated/failed counts and refresh the game list once, retaining confirmed changes if refresh fails. Disable add/edit/delete while the preview is pending or import is running so the counts cannot drift. Catalog corruption disables import in the same way as existing mutations. [FR-1, FR-4, FR-7–FR-12]

Add a **Get CSV template** action near Import. It offers a destination, uses the existing export writer's confirmation/protection for an existing file, and creates the illustrative seven-column CSV. The template action never reads or changes the catalog. Keep the view typed-bound and follow the existing layout and picker patterns. [FR-13]

## Technical decisions and rejected alternatives

| Decision and reason | Alternative rejected | FR |
| --- | --- | --- |
| Share one CSV schema/labels between serializer, parser, and template so import/export cannot drift again. | Independent hard-coded headings in each feature. | FR-2, FR-3, FR-13 |
| Use a small streaming tokenizer and existing .NET APIs; strict UTF-8 detects damaged input, and quoted newlines remain logical records. | `Split` by line/semicolon or add a CSV library solely for this feature. | FR-2, FR-3, FR-6, FR-9 |
| Parse and validate in Core, use the existing `GameRecordRules`, and keep file I/O in Infraestructure. | Interpret CSV or perform file reads in the view model. | FR-2–FR-10 |
| Build the last-valid candidate map before writing and read the catalog once; save each selected identity separately to permit partial success. | Write each encountered row immediately or wrap the entire import in one transaction. | FR-4–FR-9 |
| Compare the complete source fingerprint before writes and guard/check it during application. | Trust path, timestamp, or file length alone; they cannot prove identical content. | FR-9, FR-12 |
| Use the existing atomic export writer for a user-chosen template destination and replacement consent. | Write a template directly over a pre-existing file. | FR-13 |
| Keep approximate-size text in the UI, but remove it from the revised seven-column export. | Retain an eighth derived CSV column that import cannot accept. | FR-2, FR-3 |

## Verification strategy

- **Core unit tests:** Exact seven-column export and template bytes; reordered/mixed-case/repeated/extra/whitespace headings; blank and short rows; surplus cells; CRLF/LF, quoted semicolons/quotes/multiline Notes, optional BOM, formula-tab reversal, enum and numeric boundaries, Unicode/case-sensitive Location, last-valid duplicate, empty catalog, and count/no-op classification. [FR-2–FR-7, FR-11, FR-13]
- **Core service tests:** Preview performs no writes; decline leaves the catalog intact; confirmed create/edit preserves IDs and clears empty Notes; an identical match (including null versus empty Notes) is absent from preview counts and never calls the repository's update operation; ordinary per-row failure continues; corruption stops; changed source before first write saves nothing, while changed source or read failure during apply retains prior writes. [FR-4–FR-9, FR-12]
- **Infraestructure integration tests:** Disposable files with strict UTF-8 and fault-injected read/change points, source fingerprint checks, long streaming input without an artificial limit, and disposable SQLite catalogs for create/edit/partial failure/corruption. No user catalog or game file is touched. [FR-2, FR-3, FR-8–FR-10, FR-12]
- **Desktop functional tests:** Headless picker cancel/error, preview counts and row reasons, confirmation/cancellation, final partial-result feedback, refreshed list, disabled conflicting edits, template output/unchanged catalog, and offline-media import. [FR-1, FR-7–FR-13]
- **Final checks:** `dotnet build PSPMan.slnx`, `dotnet test PSPMan.slnx`, a measured coverage report mapping every FR, and graphical startup/manual flows when a display is available. Verify a newly exported CSV imports unchanged and the unedited template creates no records. Excel-specific checks remain explicitly pending if Excel is unavailable. [FR-1–FR-13]

## Assumptions to review before coding

- Preview counts distinct final candidates. Superseded valid rows are neither errors nor extra writes; identical matches are confirmed no-ops under FR-4 and FR-7. [FR-4, FR-5, FR-7]
- A source row is reported by the physical line on which its logical CSV record starts. Blank lines outside quotes do not count as game rows. [FR-2, FR-6]
- The template contains incomplete sample rows for the allowed Medium/Format values; importing it unchanged yields invalid-row feedback but zero creations or edits. [FR-13]
- No `AGENTS.md` update is proposed. Update `MEMORY.md` after implementation, replacing outdated export-format and import-planning notes. [FR-1–FR-13]

This document is a plan only. It does not authorize code changes; obtain approval before implementation. [FR-1–FR-13]
