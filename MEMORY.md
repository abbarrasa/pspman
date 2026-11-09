# Project memory

Durable project context; see [AGENTS.md](./AGENTS.md) for standing instructions.

## Implemented state

- PSPMan catalogs PSP games across removable media. CRUD and CSV export/import/template flows are implemented; deployment, media scanning and copying are not.
- The solution contains Core, Infraestructure and Desktop plus their xUnit suites. Preserve the existing `Infraestructure` spelling.
- Desktop composes SQLite at the OS local application-data path `PSPMan/catalog.db`. Startup/read corruption is reported without repair and disables catalog mutations.
- Avalonia typed bindings connect the catalog list/details, editor, confirmed deletion, export, import preview/confirmation/results and template destination form.
- Exact decompressed bytes and binary-unit readable sizes appear in the editor/details; CSV contains only the integer byte count.

## Domain and persistence decisions

- Identity is Medium plus ordinal case-sensitive Location, and normalized DVD Number only for DVD. Title and Format do not affect identity; edits preserve IDs.
- Location is a medium-rooted `/` path validated without filesystem access. Host mount-point interpretation and file existence checks are deliberately absent.
- DVD needs a positive Int32 number; zero normalizes to null. Non-DVD records have no DVD number. SizeBytes is positive Int64; Notes allow 2,500 UTF-16 code units.
- SQLite uses parameterized CRUD and separate case-sensitive DVD/non-DVD unique indexes. Existing catalogs are validated before transactional index migration; malformed data raise InvalidDataException without recovery.
- Subsequent connections use ReadWrite so a missing catalog is not silently recreated. Microsoft.Data.Sqlite 10.0.13 and SQLitePCLRaw.bundle_e_sqlite3 2.1.14 avoid the older native-bundle vulnerability warning.
- CRUD/import results retain confirmed saves when a subsequent refresh fails. Corruption stays distinct from ordinary storage or CSV errors.

## CSV contracts

- Shared schema: Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes. Medium labels: Dvd/Usb/MemoryCard; Format: ISO/CHD. No internal IDs or approximate size are exported.
- CSV uses semicolons, strict UTF-8 with optional import BOM/export BOM, quoted fields, doubled quotes and multiline Notes. Export prefixes formula-like text with a tab; import removes exactly one initial tab if the following first non-whitespace character is =, +, - or @, preserving other whitespace.
- Import headings are case-insensitive, reordered/extra headings are supported, all seven are required and the last repeated column wins. Whitespace in any heading fails the preview; whitespace in Medium/Format rejects that row.
- Short rows fail; surplus cells and blank/spaces-tabs-only physical lines outside quotes are ignored. Errors identify the logical record's physical starting line. Fatal quoting/decoding/read errors discard the entire preview.
- Import keeps only the last valid row per identity; invalid later duplicates cannot replace it. Identical matches, including equivalent null/empty Notes, produce no proposal, count or write. Empty Notes clear existing values.
- Preview reads one catalog snapshot without writes. Confirmation consumes immutable proposals once, creates new IDs or preserves edit IDs, and reports only completed writes. Ordinary save failures continue; corruption/source errors stop further saves and retain earlier successes.
- SHA-256 checks cover complete raw source bytes and both the held handle/current path. Apply rereads selected rows, compares them to preview, verifies before each save and after EOF. Repeated full hashing favors detection over throughput; sharing does not provide an absolute cross-platform lock.
- No fixed CSV size limit applies. The incremental tokenizer bounds its current-record buffer; candidates/errors still consume machine-dependent memory.
- The template contains every permitted Medium/Format label with other required fields blank. Importing it unchanged yields three row errors and no games; header-only input yields zero proposals/errors.
- Template saving reuses the protected export writer, destination validation, snapshots and explicit replacement consent, without reading/changing the catalog. Normal export resets the shared form's template mode.

## Desktop behavior and pitfalls

- Preview/confirm work runs away from the UI thread; results resume on the caller UI context. Cancelling preview discards late results; cancellation during saving is disabled. Add/edit/delete are guarded during evaluation, pending review and apply.
- ViewLocator replaces ViewModel with View without changing the namespace; check resolution before using navigation. Explicit availability bindings keep visible button state aligned with commands.
- Headless functional tests use Avalonia.Headless 12.1.4, FluentTheme and a nonparallel collection with disposable catalogs/files. Flush dispatcher jobs after selection before keyboard actions.
- The auto-PR workflow targets dev, assigns the pushing user and ensures review-pending; GitHub must allow Actions to create PRs. The feature skill requires an approved plan before edits; its validator needs PyYAML.

## Verification and remaining acceptance

- [CRUD](./specs/001-crud-game/coverage.md), [export](./specs/002-export-games/coverage.md) and [import](./specs/003-import-games/coverage.md) reports retain measured evidence. Import T01–T34 are recorded in [tasks](./specs/003-import-games/tasks.md).
- Import coverage run: 639 tests pass; owning-suite line/branch coverage is Core 98.09%/96.13%, Infraestructure 97.08%/85.19%, Desktop 89.65%/87.69%. Native startup/pickers and defensive paths account for remaining gaps.
- [Import graphical validation](./specs/003-import-games/manual-validation.md) observed Linux/X11 template, native pickers, preview/cancel/confirm, offline creation, duplicate handling, empty-Notes update, no-ops and source/validation errors. A graphical follow-up removed the temporary CSV after saving began: verification failed, two of 2,000 games remained saved, and later writes stopped. Decoded-stream read failure remains automated T32 evidence; physical device removal was not observed.
- Excel with Spanish regional settings remains unavailable/unverified: actual spreadsheet display, formula nonexecution and Excel-edited CSV round trip are pending. Windows/macOS native dialogs were not observed; passing CSV/headless tests do not certify those checks.

- [Device mounting spec 004](./specs/004-device-mounting/spec.md) rev 0.3, [plan](./specs/004-device-mounting/plan.md) rev 0.1 and [tasks](./specs/004-device-mounting/tasks.md) rev 0.1 are approved, including the longer single-spec task list. T01 documentary [recognition matrix](./specs/004-device-mounting/manual-validation.md) completed after individual approval: Sony sources cover five families; public USB registry entries are not full storage descriptors; 054c:01c9 is used by PSPLINK and is not sufficient mount evidence. T15 must resolve descriptor gaps; native Linux/Windows checks remain pending. No product code/tests changed or run. Stop after T01; next step is T02 plan approval.
