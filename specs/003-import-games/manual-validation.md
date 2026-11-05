# Game catalog CSV import graphical validation

## Environment and method

- Operated the actual Avalonia application on Linux/X11, including the native GTK folder and CSV pickers. Started it with `dotnet run --no-build --project src/PSPMan.Desktop/PSPMan.Desktop.csproj` and an isolated `XDG_DATA_HOME`; verified that its SQLite catalog was created inside the disposable application-data directory.
- Prepared and changed only disposable CSV fixtures. Observed the real window, its scrolling import panel, messages and game list. Used an independent Python UTF-8/semicolon CSV reader, read-only SQLite queries and SHA-256 comparisons to verify saved data and unchanged catalogs. No removable medium was connected and no user game files were accessed.
- This is graphical validation of the scenarios below, not a claim of exhaustive native-platform or spreadsheet validation. No production code was changed.

## Observed graphical results

| Scenario | Observed result | FR |
| --- | --- | --- |
| Start an empty catalog | The real window displayed the empty catalog, Import catalog, Export catalog and Get CSV template actions. | FR-1, FR-11, FR-13 |
| Get CSV template and choose a destination through the native folder picker | The form showed Save CSV import template and Save template, defaulted to `pspman-import-template.csv`, then displayed its saved path. Independent inspection found exactly the seven headings and three incomplete examples: Dvd/ISO, Usb/CHD and MemoryCard/ISO. | FR-13 |
| Choose that unedited template through the native CSV picker and preview | The summary showed 0 new, 0 modified and 3 erroneous rows. Errors identified source lines 2–4 and missing numeric values; the incomplete examples were not proposed as games. The errors and confirmation action were reachable by scrolling the bounded panel. | FR-2, FR-6, FR-7, FR-13 |
| Confirm the unedited template | The result showed 0 created, 0 updated and 0 failed. The catalog remained empty with an identical SHA-256 hash. Row-validation errors belong to the preview; they are not failed repository writes. | FR-8, FR-13 |
| Preview a mixed fixture with reordered headings and an extra column | The fixture used Notes, Format, Title, Extra, Medium, SizeBytes, Location and DvdNumber. Lower/mixed-case Medium and Format labels were accepted; the ignored extra column did not enter game data. Three final identities were proposed. | FR-2, FR-3, FR-6, FR-7 |
| Inspect multiline text, duplicates, short rows and whitespace-only lines | Notes contained semicolons, commas, a colon, quotes and an embedded newline. A valid later DVD duplicate superseded its earlier row; an invalid later duplicate did not replace it. The preview showed 3 new, 0 modified and 2 erroneous rows, identifying physical lines 7 and 8 for the invalid size and short row. The spaces/tabs-only line was ignored. | FR-3, FR-5, FR-6, FR-7 |
| Cancel that preview | The import panel closed, no games appeared, and the empty catalog hash remained identical. | FR-7 |
| Reopen, choose the mixed fixture, preview and confirm | The result showed 3 created, 0 updated and 0 failed. The list displayed the three games. Independent SQLite inspection found all three media, the last valid DVD title/size/notes and the exact multiline Notes. | FR-4, FR-5, FR-8 |
| Import formula-protected text and offline paths | A Title supplied as tab + space + `=1+1` was stored as space + `=1+1`: exactly the first protective tab was removed. All three imported locations were under `/OFFLINE/`; no media or game files were required. This verifies PSPMan's value handling, not Excel nonexecution. | FR-3, FR-10 |
| Modify only the DVD row and clear Notes | A mixed-case `tItLe` heading was accepted. Preview showed 0 new, 1 modified and 0 erroneous rows. After confirmation, SQLite retained every original ID, changed the DVD title and stored empty Notes; the other two games remained unchanged. | FR-2, FR-4, FR-7, FR-8 |
| Preview the same final values again | The summary showed 0 new, 0 modified and 0 erroneous rows: identical matches were omitted from the proposed counts. | FR-4, FR-7 |
| Change the CSV after preview, then confirm | The window reported “The CSV source has changed since it was evaluated.” The game list and catalog hash remained unchanged. | FR-9, FR-12 |
| Restore and preview a valid CSV, delete it, then confirm | The window reported “Unable to open the CSV source.” No changes were saved. | FR-9, FR-12 |
| Include whitespace in a heading | `Title ` failed the entire preview with a line-1/column-1 heading-whitespace error. No proposals were available for confirmation. | FR-2, FR-9 |
| Include whitespace in Medium and Format values | ` Usb` and `ISO ` yielded two row errors identifying lines 2 and 3 and the affected fields. The summary showed 0 new, 0 modified and 2 erroneous rows. | FR-6 |
| Preview and confirm a header-only CSV | Both preview and final counts were zero. The three existing games and catalog bytes remained unchanged. | FR-11 |
| Cancel the native CSV picker | The import panel closed and the three saved games remained visible. The catalog hash still matched the baseline taken before the error/header-only scenarios. | FR-1 |

### Follow-up: source interruption after saving began

A subsequent graphical check used a fresh disposable catalog and 2,000 valid records in a 544,949-byte CSV. The workstation exposed four logical processors and approximately 4.6 GB available RAM at the start; this modest fixture avoided a stress test. A read-only SQLite watcher removed only that temporary CSV after observing the first committed record.

- Preview showed 2,000 new records and no row errors. Import was confirmed through the real window.
- The watcher observed one saved record before removing the file; a second record completed before the next source check detected the missing path.
- The real window reported **2 created, 0 updated, 0 failed** and **Unable to verify the CSV source.** Both saved games appeared in the list.
- Independent SQLite checks found exactly the first two games, with no later writes after the error. The reader's held file descriptor can remain readable after unlink on Linux; failure occurred when reopening the current path for verification.
- This manually verifies a source-verification failure **during** import and retention of partial saves (FR-8, FR-9, FR-12). It does **not** demonstrate a decoded-stream read error or physical device failure. The zero failed count correctly excludes source failures from per-record repository failures.

No product deviation was observed in these graphical scenarios. Error messages, counts and actions were in English; native GTK dialog chrome followed the host's Spanish locale. The application was closed after validation.

## Automated evidence kept separate from manual observations

- A decoded-stream read error **during** saving was not injected into the real graphical process; the follow-up above observed a path-verification failure after saving began. T32's `MidApplyReadFailureDisplaysPartialResultsAndRetainsSavedSqliteRecord` in [headless flow tests](../../tests/PSPMan.Desktop.Tests/GameCatalogImportFlowTests.cs) wraps the real file reader, fails decoded reads immediately after the first selected record, and verifies one saved SQLite record, visible partial counts/source error, no later save, and reader disposal. This remains automated evidence for FR-8/FR-9, not a manually observed device-disconnection result.
- Ordinary per-record save failures, catalog corruption, protection against replacing an existing template, repeated heading precedence, and changes during saving have further automated evidence in the [coverage report](./coverage.md). They were not all repeated through native dialogs during this walkthrough.
- T33 recorded 639 passing tests and owning-suite coverage. This documentation task reruns build/tests; it does not change the code or coverage exclusions.

## Pending spreadsheet and platform acceptance

- Microsoft Excel was not available. LibreOffice was present, but its Calc launcher was absent; it was not used as a substitute for Excel.
- **Pending:** open the template and a catalog export in Excel configured for Spanish regional settings; verify seven English-headed columns, every permitted Medium/Format label, integer SizeBytes, punctuation/Unicode/multiline Notes, and literal display/nonexecution of formula-like text.
- **Pending:** save an edited CSV from that Excel installation and import it into PSPMan; confirm the intended values and counts. Independent CSV parsing and passing automated tests do not establish Excel's actual save/render behavior.
- Windows/macOS native pickers and actual removable-device removal were not observed on this Linux workstation. The documented cross-platform file-sharing limitation remains applicable.
