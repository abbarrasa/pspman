# Game Record CRUD Implementation Plan

Status: proposed implementation plan. This document does not authorize code changes. It follows the [current specification](./spec.md) and the [project constitution](../../docs/constitution.md).

## Implementation approach

Keep record values, validation, identity rules, and catalog operations in Core. Define a Core persistence contract, implement it with SQLite in Infraestructure, and compose it in Desktop. The UI displays the catalog and collects input; it never decides whether two records are duplicates or accesses game files. Preserve the existing `Infraestructure` spelling. No media scan, file existence check, extraction, transfer, or PSP space check is part of this work. [FR-1–FR-11]

## Files and responsibilities

| File (repository-relative) | Change and responsibility | FR |
| --- | --- | --- |
| `src/PSPMan.Core/Games/GameRecord.cs` | Create the record and input data types, stable record ID, Medium and Format values, and optional DVD Number. | FR-2, FR-3, FR-5, FR-7 |
| `src/PSPMan.Core/Games/GameRecordRules.cs` | Create pure validation, zero-to-null DVD normalization, path checks, and duplicate-identity rules. | FR-2, FR-4, FR-6 |
| `src/PSPMan.Core/Games/IGameRecordRepository.cs` | Create the catalog operations contract; keep storage details out of Core. | FR-1, FR-5–FR-11 |
| `src/PSPMan.Core/Games/GameCatalogService.cs` | Create use cases for list, read, create, edit, and delete; apply rules and return expected failures for the UI to display. | FR-1, FR-4–FR-11 |
| `src/PSPMan.Infraestructure/PSPMan.Infraestructure.csproj` | Add the specified Microsoft.Data.Sqlite dependency only. | FR-1, FR-5–FR-7, FR-8, FR-10, FR-11 |
| `src/PSPMan.Infraestructure/Games/SqliteGameRecordRepository.cs` | Create the local catalog, schema, case-sensitive uniqueness constraints, and parameterized CRUD operations; preserve corrupt data without recovery. | FR-1, FR-3, FR-5–FR-11 |
| `src/PSPMan.Desktop/App.axaml.cs` | Compose the repository, service, and main view model at startup; surface catalog startup errors without replacing existing data. | FR-1, FR-10, FR-11 |
| `src/PSPMan.Desktop/ViewModels/GameEditorViewModel.cs` | Create editable field state and field-level error display; submit data to the Core service. | FR-2–FR-4, FR-7, FR-9 |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.cs` | Replace the sample greeting with list, selection, create/edit/save, delete confirmation, cancel, empty-state, and error state. | FR-1, FR-3, FR-5–FR-11 |
| `src/PSPMan.Desktop/Views/MainWindow.axaml` | Replace the sample control with a typed-bound list, record details/editor, validation messages, and inline delete confirmation. | FR-1–FR-10 |
| `tests/PSPMan.Core.Tests/GameRecordRulesTests.cs` | Add rule boundary tests. | FR-2–FR-4, FR-6 |
| `tests/PSPMan.Core.Tests/GameCatalogServiceTests.cs` | Add catalog use-case tests with an in-memory fake repository. | FR-1, FR-5–FR-11 |
| `tests/PSPMan.Core.Tests/UnitTest1.cs` | Remove the empty sample test. | — |
| `tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj` and `SqliteGameRecordRepositoryTests.cs` | Create xUnit/coverlet integration tests against disposable local databases. | FR-1, FR-5–FR-7, FR-8, FR-10, FR-11 |
| `tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj`, `TestAppBuilder.cs`, and `GameCatalogFlowTests.cs` | Create xUnit/coverlet headless functional tests for controls, bindings, validation, confirmation, and errors. | FR-1–FR-11 |
| `PSPMan.slnx` | Include both new test projects in the solution. | FR-1–FR-11 |

The existing `ViewLocator` is not used by this single-window flow, so it needs no change. After implementation, update `MEMORY.md` with the actual state and any durable decisions; no `AGENTS.md` change is proposed. Any accepted behavior change must also update `spec.md`. [FR-1–FR-11]

## Pure logic functions (pseudocode)

1. **Normalize DVD number:** If the entered number is zero, treat it as null; otherwise retain a positive integer or return a field error. A DVD requires a positive number; USB and Memory Card require null. [FR-2, FR-4]
2. **Validate record input:** Reject a Title containing only Unicode whitespace, missing required fields, unsupported Medium or Format, non-positive Size in bytes, Notes longer than 2,500 UTF-16 code units, or an invalid DVD number. Return field-specific errors without changing a saved record. [FR-2–FR-4, FR-7]
3. **Validate Location:** Require a path rooted within the medium, beginning with `/` and ending in a file-name segment. Split it into segments; reject empty, `.` or `..` segments and control characters. Do not consult the host file system or require a connected medium. Preserve the entered case. [FR-3, FR-4, FR-9]
4. **Compare record identity:** Compare Medium, then Location using exact case-sensitive text, then DVD Number when Medium is DVD. Ignore Title, Format, Size, and Notes; exclude the record being edited from the duplicate search. [FR-6, FR-7]

## Use-case algorithms (pseudocode)
5. **Save:** Validate input; if errors exist, report them. Check for another record with the same identity; if found, report a duplicate. Otherwise create or update the record atomically, reload the list, and report success only after persistence succeeds. If persistence fails, keep the previous saved state and show the failure. [FR-1, FR-4–FR-7, FR-9, FR-10]
6. **Delete:** Mark the selected record as pending deletion; cancellation clears that state. On confirmation, delete only its catalog row. Refresh the list after success; on failure, report the error and do not claim deletion succeeded. Never access the referenced game file. [FR-8, FR-10]
7. **Corrupt catalog:** If opening or reading existing catalog data fails because of corruption, show an error and stop catalog mutations for that session. Do not recreate, repair, discard, or overwrite the existing catalog. [FR-10, FR-11]

## Interface

Use the existing main window: a game list with an empty state, a detail area, and an Add/Edit form. Show required field markers, Medium and Format choices, DVD Number only when relevant, user-entered decompressed Size in bytes, field errors, and a general operation error. An inline confirmation area offers Confirm and Cancel before deletion. Bind controls to view-model state with typed Avalonia bindings; show English text throughout. Offline media do not change the form or block saving. No navigation framework or extra dialog package is needed. [FR-1–FR-10]

## Technical decisions and rejected alternatives

- **Direct SQLite access through Microsoft.Data.Sqlite.** One local catalog under the user's application-data directory, with a small schema and parameterized operations. This matches the intended stack and avoids adding an ORM or storing the catalog beside the executable. [FR-1, FR-5–FR-11]
- **Two case-sensitive uniqueness rules.** DVD records use Medium + DVD Number + Location; USB and Memory Card records use Medium + Location. Reject duplicates in Core for clear feedback and enforce the same keys in the database. A single ordinary unique index including nullable DVD Number is rejected because [SQLite treats NULL values as distinct in unique indexes](https://www.sqlite.org/lang_createindex.html). [FR-6, FR-10]
- **Portable medium-rooted paths as text.** Store paths with `/` separators and no host mount prefix; compare the stored path exactly, including case. Host-absolute paths and automatic file-system normalization are rejected because media can be offline or mounted differently. Do not infer Format from the file extension: the specified example ends in `.rar`. [FR-3, FR-4, FR-6, FR-9]
- **One typed-bound window with inline confirmation.** This uses the existing screen and avoids navigation or modal-dialog dependencies; business decisions remain in Core. [FR-1–FR-10]
- **Headless UI tests with the existing xUnit generation.** Add Avalonia.Headless to the Desktop test project and use its manual session setup. This tests bindings and controls without a display; the alternative Avalonia.Headless.XUnit integration would require moving to xUnit 3 under Avalonia 12, while the current project uses xUnit 2. [Avalonia's manual headless setup](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) and [Avalonia 12 test-framework change](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes) support this choice. [FR-1–FR-11]

## Test and verification strategy

- **Unit:** Test every pure rule and use case: all-whitespace Titles; DVD null/zero/negative/positive; Medium and Format options; malformed or case-different paths; Size boundaries; Notes at 2,500/2,501 UTF-16 code units; duplicate create/edit; cancel/confirm deletion; failed repository calls. No test requires a mounted medium. [FR-1–FR-11]
- **Integration:** Use isolated temporary SQLite catalogs to test schema creation, round-trip values, persistence after reopening, both uniqueness rules, exact case comparison, atomic updates/deletes, storage failures, and corrupt-data reporting without overwrite. [FR-1, FR-3, FR-5–FR-11]
- **Functional:** Drive the Avalonia controls headlessly through create, list/view, edit, invalid input, duplicate rejection, confirmed/cancelled deletion, empty state, unavailable media, startup corruption, and visible error messages. Also perform the specification's manual main-flow demonstration on an available graphical desktop. [FR-1–FR-11]
- **Coverage and checks:** Run `dotnet build PSPMan.slnx` and `dotnet test PSPMan.slnx`; collect line and branch coverage using the existing coverlet tooling, produce a coverage report, and justify significant gaps. Review the FR-to-test mapping before declaring the feature complete. [FR-1–FR-11]

## Approval points

The specification has no open questions, but has not received explicit final approval. Review the proposed portable `/` path syntax, the local catalog location, and the added Avalonia.Headless test dependency before implementation. This plan does not include code changes, and no implementation begins while the user's no-code instruction remains in force. [FR-3, FR-4, FR-9]
