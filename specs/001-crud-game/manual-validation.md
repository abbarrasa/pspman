# Game catalog CRUD graphical validation

## Environment

- Ran the actual Avalonia Desktop application on an X11 graphical session with a temporary, isolated application-data directory. The user's catalog was not used.
- Observed the window after each action and read the disposable SQLite catalog in read-only mode to check persisted results.
- Used a medium-rooted Location with no corresponding file on the host. This exercised the offline workflow; no removable medium was required.

| Step | Observed result | Requirements |
| --- | --- | --- |
| Open a fresh catalog | The window showed “No games in the catalog.” and an available editor. | FR-1 |
| Add `Offline Quest`, USB, `/GAMES/Offline Quest.rar`, ISO, 1,073,741,824 decompressed bytes, and `Offline demo` notes | One selected row appeared. Details showed every saved value, including `1 GiB`; the SQLite row contained the entered values. | FR-2, FR-3, FR-5, FR-9 |
| Submit `Another Title` with the same USB Location, CHD, and a different size | The window explained the duplicate identity; the first row and its saved values stayed unchanged, and SQLite still contained one row. | FR-6 |
| Edit the selected record to `Offline Quest Updated`, `/GAMES/Offline Quest v2.rar`, and 2,147,483,648 bytes while its file remained absent | The list, details, and SQLite row showed the new values and `2 GiB`. | FR-3, FR-7, FR-9 |
| Request deletion, then cancel | The confirmation named the selected game and said the game file would not be deleted. Cancellation closed the prompt and retained the row in the window and SQLite. | FR-8 |
| Request deletion again, then confirm | The window returned to its empty state and SQLite contained zero game rows. No game file was created or accessed in this offline demonstration; the [headless file-sentinel test](../../tests/PSPMan.Desktop.Tests/GameCatalogFlowTests.cs) covers preservation of an existing file. | FR-8 |

No product deviation was observed in the manual flow. FR-4, FR-10, and FR-11, including validation, storage failures, and corrupt-catalog handling, have automated acceptance evidence in the [coverage report](./coverage.md). The graphical exercise was on Linux/X11; the automated suite remains the available evidence for other supported desktop platforms.
