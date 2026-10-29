# Game catalog CSV export coverage report

## Verification

- `dotnet build PSPMan.slnx`: passed with 0 warnings and 0 errors.
- `env -u DISPLAY -u WAYLAND_DISPLAY dotnet test PSPMan.slnx --no-restore --collect:'XPlat Code Coverage' --results-directory TestResults/T22-report -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura`: 278 passed, 0 failed, 0 skipped (Core 123, Infraestructure 38, Desktop 117).
- Measurements use each production project's package rate from its owning xUnit suite. Dependent suites also instrument referenced assemblies, so their duplicate measurements are not added. The raw Cobertura XML is in the ignored `TestResults/T22-report/` directory.

| Production project | Line coverage | Branch coverage | Owning suite |
| --- | ---: | ---: | --- |
| Core | 269/273 (98.53%) | 106/110 (96.36%) | Core.Tests |
| Infraestructure | 261/265 (98.49%) | 39/44 (88.63%) | Infraestructure.Tests |
| Desktop | 792/880 (90.00%) | 306/354 (86.44%) | Desktop.Tests |

## Functional requirement evidence

These are representative passing tests, with unit, integration, and headless functional checks where applicable.

| Requirement | Evidence |
| --- | --- |
| FR-1 — choose destination or cancel without changes | [Folder picker tests](../../tests/PSPMan.Desktop.Tests/GameExportFolderPickerTests.cs) cover local choice and cancellation; [headless flow tests](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) cover folder/name controls and unchanged files after cancellation; [destination tests](../../tests/PSPMan.Desktop.Tests/GameExportDestinationTests.cs) cover invalid names and paths. |
| FR-2 — all saved rows, fixed fields and order, no IDs | [CSV serializer tests](../../tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs) check column projection and input order; [SQLite-backed headless flow](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) checks every offline record in saved order regardless of selection. |
| FR-3 — Spanish Excel CSV dialect and sizes | [CSV serializer tests](../../tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs) check UTF-8 BOM, semicolons, CRLF, English headings, invariant integers, and blank non-DVD numbers; [size formatter tests](../../tests/PSPMan.Core.Tests/ByteSizeFormatterTests.cs) check binary units; [headless flow](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) checks exported bytes. |
| FR-4 — punctuation and multiline text in one cell | [CSV serializer tests](../../tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs) check escaping, doubled quotes, Unicode, and empty Notes; [SQLite-backed headless flow](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) parses an exported file into eight cells per logical row. |
| FR-5 — formula-like text stays literal | [CSV serializer tests](../../tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs) check all four triggers after leading whitespace and the quoted protective tab; [SQLite-backed headless flow](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) checks protected Title and Notes in a real export with unchanged saved values. Excel display and execution remain a manual T23 check. |
| FR-6 — explicit replacement consent | [Writer integration tests](../../tests/PSPMan.Infraestructure.Tests/AtomicCsvExportWriterTests.cs) cover refusal, approval, and destination changes after inspection; [headless flow tests](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) cover visible confirmation, decline, approval, and unchanged declined bytes. |
| FR-7 — success only after publication, source preservation | [Writer integration tests](../../tests/PSPMan.Infraestructure.Tests/AtomicCsvExportWriterTests.cs) check complete publication; [SQLite-backed headless flow](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) checks success path, unchanged catalog/game-file bytes, and unchanged saved values. |
| FR-8 — failure without false success or partial replacement | [Writer integration tests](../../tests/PSPMan.Infraestructure.Tests/AtomicCsvExportWriterTests.cs) cover staging/publication failures, cleanup, and original-byte preservation; [view-model export tests](../../tests/PSPMan.Desktop.Tests/MainViewModelExportTests.cs) cover read, destination, and publication errors; [headless flow tests](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) cover corrupt reads and failed writes with visible errors and no false success. |
| FR-9 — header-only empty catalog | [CSV serializer tests](../../tests/PSPMan.Core.Tests/GameCatalogCsvSerializerTests.cs) check exact empty bytes; [SQLite-backed headless flow](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) checks export without selection and no source changes. |

## Remaining coverage gaps

- Core misses four lines and four branches in its owning suite: two `ExportDestinationSnapshot` lines exercised instead by the writer integration suite, and the serializer's invalid Medium/Format fallbacks. Valid persisted game records cannot reach those fallback cases.
- Infraestructure misses four lines in unusual SQLite integrity-check and CLR conversion-error handling, plus five branches chiefly in defensive writer paths. Integration tests cover malformed catalogs, ordinary storage failures, failed staging/publication, destination races, and unchanged existing bytes; forcing the remaining exceptional paths would need additional fault injection.
- Desktop misses 88 lines and 48 branches. The largest gaps are graphical application startup, `ViewLocator`, and real Avalonia storage-provider interaction, because headless tests create the window with a test application and picker. Other gaps include defensive export-command guards and rare destination-resolution error paths. The UI and file failure flows are covered headlessly; the real Spanish Excel and graphical walkthrough remains T23.
- These figures measure code execution, not Excel compatibility or safety on their own. No production-code change was needed for T22.
