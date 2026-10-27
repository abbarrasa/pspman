# Game catalog CRUD coverage report

## Verification

- `dotnet build PSPMan.slnx`: passed with 0 warnings and 0 errors.
- `env -u DISPLAY -u WAYLAND_DISPLAY dotnet test PSPMan.slnx --no-restore --collect:'XPlat Code Coverage' --results-directory TestResults/T24-report -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura`: 178 passed, 0 failed, 0 skipped (Core 88, Infraestructure 28, Desktop 62).
- Measurements use the Cobertura package rates from each project's own test suite. The same production assembly is also instrumented by dependent suites; those duplicate measurements are not added together. Raw XML is in the ignored `TestResults/T24-report/` directory.

| Production project | Line coverage | Branch coverage | Owning test suite |
| --- | ---: | ---: | --- |
| Core | 200/200 (100%) | 64/64 (100%) | Core.Tests |
| Infraestructure | 199/203 (98.02%) | 26/28 (92.85%) | Infraestructure.Tests |
| Desktop | 529/581 (91.04%) | 220/254 (86.61%) | Desktop.Tests |

## Functional requirement evidence

The links identify representative passing tests; each requirement has additional unit, integration, or headless functional tests where appropriate.

| Requirement | Evidence |
| --- | --- |
| FR-1 — list and empty state | [Core list tests](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [SQLite fresh catalog](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless empty/list tests](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-2 — fields and permitted choices | [record and rule tests](../../tests/PSPMan.Core.Tests/GameRecordRulesTests.cs), [editor choices and DVD number](../../tests/PSPMan.Desktop.Tests/GameEditorViewModelTests.cs), [headless editor controls](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-3 — saved details, medium-rooted path, decompressed bytes | [path rules](../../tests/PSPMan.Core.Tests/GameRecordRulesTests.cs), [SQLite round-trip](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless DVD and Memory Card details](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-4 — validation and no invalid save | [boundary rules](../../tests/PSPMan.Core.Tests/GameRecordRulesTests.cs), [service validation](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [headless field errors](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-5 — create and display | [service create](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [SQLite persistence](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless create](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-6 — duplicate identity and feedback | [identity and service tests](../../tests/PSPMan.Core.Tests/GameRecordRulesTests.cs), [SQLite uniqueness tests](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless duplicate create/edit](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-7 — edit without file access | [service edit](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [SQLite update](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless offline edit](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-8 — confirm/cancel record-only delete | [service delete](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [SQLite delete](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless cancel/confirm with file sentinel](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-9 — offline create/view/edit | [service offline operations](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [headless offline edit and Memory Card view](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-10 — operation failure without false success | [service failure cases](../../tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs), [SQLite failure cases](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless failed save/delete](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |
| FR-11 — corrupt data reported and preserved | [Core corruption cases](../../tests/PSPMan.Core.Tests/GameCatalogCorruptionTests.cs), [SQLite corruption cases](../../tests/PSPMan.Infraestructure.Tests/SqliteGameRecordRepositoryTests.cs), [headless corrupt startup](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) |

## Remaining coverage gaps

- Infraestructure misses four lines and two branches, chiefly the unusual `PRAGMA integrity_check` result and CLR conversion-exception wrappers. Existing integration tests cover malformed databases, saved-field corruption, failed operations, and preservation of original bytes. Forcing those remaining SQLite/CLR outcomes would require a test seam or database fault injection beyond this task.
- Desktop misses 52 lines and 34 branches. Most uncovered lines are the real graphical bootstrap (`App`, `Program`) and unused `ViewLocator`, because headless tests supply a test application and instantiate the window directly. The other gaps include the default application-data path, defensive command guards, and uncommon post-save refresh combinations. Existing tests cover visible CRUD, validation, offline media, failures, and corrupt startup; the graphical main-flow demonstration remains T25.
- Coverage is a measure of executed code, not a substitute for the acceptance checks above. No production-code change was needed for T24.
