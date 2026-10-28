# Game Catalog CSV Export Plan

This plan follows the [constitution](../../docs/constitution.md) and [specification](./spec.md). It describes implementation only; no production code is part of this planning task. Export reads the saved catalog afresh, formats a complete CSV without media access, and replaces a destination only after all content is ready. Existing CRUD behavior and catalog data remain unchanged. [FR-1–FR-9]

## Files and responsibilities

| File | Proposed change | Coverage |
| --- | --- | --- |
| `src/PSPMan.Core/Games/ByteSizeFormatter.cs` | Move the existing pure binary-unit formatter out of the Desktop view-model file so the detail view and CSV use identical text. | FR-2, FR-3 |
| `src/PSPMan.Core/Games/GameCatalogCsvSerializer.cs` | Add pure projection and CSV encoding: fixed English headers and column order, invariant numbers, escaping, Excel text protection, UTF-8 BOM, and header-only empty output. | FR-2–FR-5, FR-9 |
| `src/PSPMan.Core/Games/IGameCatalogExportWriter.cs` | Define the narrow destination-check/write contract used by the view model; Core contains no filesystem implementation. | FR-6–FR-8 |
| `src/PSPMan.Infraestructure/Games/AtomicCsvExportWriter.cs` | Check the destination and stage complete bytes beside it; publish a new file or replace an approved existing file only after a successful write. Report failures and remove its own staging file. | FR-6–FR-8 |
| `src/PSPMan.Desktop/ViewModels/GameEditorViewModel.cs` | Remove the old local size formatter and use the shared Core formatter, without changing the visible size format. | FR-3 |
| `src/PSPMan.Desktop/Services/IGameExportFolderPicker.cs` and `AvaloniaGameExportFolderPicker.cs` | Provide a Desktop-only folder-choice abstraction and its Avalonia implementation; return a local folder path or cancellation without writing any file. | FR-1, FR-8 |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.cs` | Hold export form/status and confirmation state; coordinate folder choice, filename validation, fresh catalog read, serialization, consent, and the writer. Disable export when the catalog is unavailable or corrupt. | FR-1, FR-2, FR-6–FR-9 |
| `src/PSPMan.Desktop/Views/MainWindow.axaml` | Add a typed-bound Export action, folder/name form, replacement confirmation, and English success/error feedback in the existing window. | FR-1, FR-6–FR-9 |
| `src/PSPMan.Desktop/App.axaml.cs` and `CatalogStartup.cs` | Compose the window-bound folder picker and file writer with the main view model; retain the existing temporary-root test seam. | FR-1, FR-7, FR-8 |
| `tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs` | Test pure formatting, exact columns, empty export, punctuation, line breaks, Unicode, and formula-like text. | FR-2–FR-5, FR-9 |
| `tests/PSPMan.Core.Tests/ByteSizeFormatterTests.cs` | Protect existing binary-unit output after moving the formatter. | FR-3 |
| `tests/PSPMan.Infraestructure.Tests/AtomicCsvExportWriterTests.cs` | Use disposable directories to test new files, approved replacement, unapproved replacement, write failures, cleanup, and old-byte preservation. | FR-6–FR-8 |
| `tests/PSPMan.Desktop.Tests/MainViewModelExportTests.cs` and `GameCatalogFlowTests.cs` | Test coordination with fake picker/writer and headless controls: cancellation, all saved rows regardless of selection, overwrite choice, empty catalog, success, read/corruption/write errors, and disabled state. | FR-1, FR-2, FR-6–FR-9 |
| `specs/002-export-games/coverage.md` and `manual-validation.md` | Record FR-to-test evidence, line/branch coverage and justified gaps; document a real Excel Spanish-locale walkthrough. | FR-1–FR-9 |
| `MEMORY.md` | Replace the draft-only export note with actual behavior and durable limitations after implementation. | FR-1–FR-9 |

No `AGENTS.md` change or new package is proposed. The existing `Infraestructure` project spelling stays unchanged. The catalog repository and database schema need no modification. [FR-2, FR-7]

## Pure logic functions

1. `ProjectRecord(record)`: return exactly the eight specified display values, including blank DVD Number/Notes, English Medium/Format labels, invariant byte count, and the shared readable binary size. Never include the internal ID. [FR-2, FR-3]
2. `NeedsExcelTextProtection(text)`: inspect the first non-whitespace character of an untrusted text value for `=`, `+`, `-`, or `@`. Apply only to text cells, not the positive numeric columns. [FR-5]
3. `ProtectExcelText(text)`: prepend an Excel-resistant tab inside the quoted cell only when protection is needed. This changes the underlying CSV cell bytes but keeps the original text visually readable; acceptance must verify that Excel does not execute it. [FR-5]
4. `EscapeCsvField(text)`: double embedded quotes and wrap a field in double quotes when it contains a semicolon, quote, line break, or a protective tab. Quoting all text fields is also valid if it simplifies the serializer; tests pin the chosen output. [FR-4, FR-5]
5. `SerializeCatalog(records)`: produce the fixed header and one row per record in input order, with semicolons, consistent row terminators, and UTF-8 BOM. An empty input still produces the header. [FR-2–FR-4, FR-9]
6. `ValidateExportName(name)`: accept a single, nonempty `.csv` filename and reject path separators, traversal, and names that would address the catalog itself. The chosen folder supplies the directory. [FR-1, FR-7, FR-8]

## Algorithms (pseudocode)

### Request and confirmation [FR-1, FR-2, FR-6–FR-9]

```text
on Export catalog:
    show export form with suggested name "pspman-games.csv"
    ask user to choose a folder through the desktop folder picker
    if picker is cancelled: close export flow; leave all files unchanged
    if folder has no usable local path: show an error; do not write
    validate the entered filename and resolve it inside the selected folder
    if invalid or protected catalog destination: show an error; do not write
    read all saved records through GameCatalogService.List()
    if read fails: show the read/corruption error; do not write
    serialize the complete read result in its returned order
    if destination exists: show replacement confirmation with its name
    if user declines: close confirmation; leave destination unchanged
    ask writer to publish bytes with the recorded overwrite consent
    if writer fails: show error; do not report success
    otherwise: show success and chosen destination
```

The fresh service read avoids exporting a stale UI list after a failed refresh. The picker and writer never inspect removable media or modify catalog records. A corruption result follows the existing session behavior that blocks catalog actions. [FR-2, FR-7, FR-8]

### Safe publication [FR-6–FR-8]

```text
write(destination, completeBytes, consent):
    if destination exists and consent is absent: report "confirmation required"
    create a uniquely named staging file in destination's directory
    write every byte; flush and close staging file
    recheck destination existence and consent before publication
    if new destination: move staging file into place without overwrite
    if approved existing destination: replace it with the staged file
    report success only after publication completes
    on any failure: remove staging file if owned; report failure
```

Staging in the same directory avoids a cross-volume copy when publishing. Tests must show that ordinary read, serialization, and write failures leave an existing file byte-for-byte unchanged and do not leave a new partial export. If another process changes the destination after confirmation, abort instead of silently overwriting unreviewed content where a reliable change check is available. [FR-6–FR-8]

## Interface

The main window gets an **Export catalog** action available even for an empty catalog, but disabled when the catalog is unavailable/corrupt. Its typed-bound form asks for a folder and `.csv` name, provides an Export/Cancel choice, and shows an inline replacement confirmation when needed. The confirmation identifies the destination and offers Replace/Cancel. A status area shows the saved destination only after successful publication, or an actionable English error after failure. No game selection is required. The folder picker is the only view-specific operation; export decisions and state remain in the view model. [FR-1, FR-2, FR-6–FR-9]

## Technical decisions and alternatives

- **Use the existing catalog read path and a pure serializer.** Core owns CSV value rules; Infraestructure owns file writes; Desktop owns the picker and state. Reading from `Games` in the view model is rejected because that list can be stale after a failed refresh. [FR-2–FR-5, FR-7, FR-8]
- **Use a folder picker plus an explicit filename field.** [Avalonia's storage provider](https://docs.avaloniaui.net/docs/services/storage/storage-provider) supports desktop folder selection, and its [storage items expose local paths where available](https://docs.avaloniaui.net/docs/services/storage/storage-item). A save picker followed by direct writing to its returned file handle is rejected because that would bypass staged replacement and could leave a partial destination on failure; folder selection itself does not create the export file. Reject a nonlocal picker result rather than falling back to a direct stream write. [FR-1, FR-6, FR-8]
- **Stage then publish.** Use standard .NET file APIs, with a no-overwrite move for new destinations and replacement only after explicit consent. Direct writing, delete-then-move, and copy-overwrite are rejected because they can expose partial output or destroy existing content on failure; [.NET documents the copy/delete limitation](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move). [FR-6–FR-8]
- **Keep the fixed dialect in a small tested serializer.** Semicolon separation and UTF-8 BOM match the chosen Spanish Excel workflow; [Microsoft documents BOM for normal UTF-8 CSV opening](https://support.microsoft.com/en-gb/excel/opening-csv-utf-8-files-correctly-in-excel) and [Excel's regional list separator behavior](https://support.microsoft.com/en-us/excel/get-started/import-or-export-text-txt-or-csv-files). A CSV package and a user-configurable dialect add dependencies and scope without current need. [FR-3, FR-4]
- **Protect formula-like text inside quoted cells.** [OWASP documents the Excel-resistant tab prefix and its downstream-data trade-off](https://community.owasp.org/attacks/CSV_Injection). Plain CSV quoting or a leading apostrophe alone is rejected because those protections may fail when Excel re-saves and reopens the CSV. The tab is export-only; the catalog value remains unchanged. [FR-4, FR-5, FR-7]
- **Share the existing size formatter.** Move its pure code to Core and keep Desktop output unchanged. Duplicating the binary-unit calculation risks disagreement between the visible details and the CSV. [FR-3]

## Test and verification strategy

- **Unit:** Exact header and column order; all visible fields and no ID; DVD/non-DVD and empty Notes; integer and binary sizes; empty catalog; duplicate titles; Unicode; commas/colons/semicolons/quotes/CRLF; formula triggers after whitespace; safe text that must not gain a protective prefix. Verify stable byte output including BOM. [FR-2–FR-5, FR-9]
- **Integration:** Disposable local directories and files; no destination created on cancellation; no overwrite without consent; approved replacement; missing/unwritable folder, failed staging write, interrupted write, and destination changed before commit; unchanged original bytes and staging cleanup. Use a deterministic write-failure seam rather than platform-dependent permission tests. [FR-1, FR-6–FR-8]
- **Functional:** Headless Avalonia tests with a fake folder picker/writer drive real bindings for empty/nonempty export, filename validation, cancellation, confirmation, success, and visible errors. Use SQLite-backed tests for full saved-order export, corruption, and offline records. [FR-1, FR-2, FR-6–FR-9]
- **Manual:** On a graphical desktop, choose a destination and open the result in Excel configured for Spanish regional settings. Check one record per row, eight English headings, bytes/readable size, notes with semicolons/quotes/newlines, formula-like text shown literally, replacement and cancellation, and the empty header-only case. If Excel is unavailable, record the manual acceptance item as pending rather than claiming it passed. [FR-1–FR-9]
- **Completion:** Run the full solution build and tests, collect line/branch coverage with existing coverlet tooling, map each FR to passing evidence, justify material gaps, and update project memory to the observed state. [FR-1–FR-9]

## Approval points

The specification is drafted and this document does not authorize coding. Before implementation, approve the spec and confirm the two explicit interpretations in this plan: a folder picker plus filename field satisfies destination choice, and a protective tab may appear in the CSV's underlying text while Excel displays the original value safely. No code or package changes are made by this planning task. [FR-1, FR-5]
