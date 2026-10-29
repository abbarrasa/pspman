# Game catalog CSV export graphical validation

## Environment and scope

- Ran the actual Avalonia application on Linux/X11. A bubblewrap bind mount redirected its catalog path to a disposable SQLite database; the existing user catalog's modification time remained unchanged. The export destination was a disposable local directory. No removable medium was connected.
- Opened and operated the real folder picker and export form. Inspected the generated files with an independent UTF-8/semicolon CSV reader and compared catalog and destination hashes before and after relevant actions.
- Excel was unavailable. LibreOffice Writer was installed, but Calc was not; attempting to open the CSV in LibreOffice produced a generic filter-selection dialog. Spreadsheet rendering and formula execution were therefore **not observed**.

| Step | Observed result | Requirements |
| --- | --- | --- |
| Open the empty isolated catalog and cancel the folder picker | The window displayed “No games in the catalog.” Cancel closed the export form, produced no file, and left the catalog unchanged. | FR-1, FR-9 |
| Choose a disposable folder and export the empty catalog | The window showed the completed destination. The 108-byte file contained a UTF-8 BOM and only the eight English semicolon-separated headings with CRLF; there was no data row. | FR-1, FR-3, FR-7, FR-9 |
| Add two valid records to the disposable SQLite catalog and reopen the application | The window listed both records in saved order without game files. Selecting the DVD record showed DVD number 2, 1,073,741,824 decompressed bytes, `1 GiB`, and the multiline Notes. | FR-2, FR-3 |
| Choose a folder and a custom filename, then export the nonempty catalog | The window reported the chosen path only after publication. The file had a UTF-8 BOM, eight columns, one header row, and two data rows in saved order, without internal IDs. The non-DVD DVD Number cell was empty; sizes were `1073741824`/`1 GiB` and `2048`/`2 KiB`. | FR-1–FR-3, FR-7 |
| Inspect special text with an independent CSV reader | Notes containing `;`, comma, colon, quotes, CRLF, and `í` remained one logical cell with the original value. Formula-like Title `=1+1` and Notes beginning with whitespace then `+` were quoted and prefixed with an export-only tab. The saved catalog hash remained unchanged. Excel's visual display and nonexecution are pending. | FR-4, FR-5, FR-7 |
| Attempt to export over the existing empty file, then choose “Keep existing file” | The window named the existing destination and offered Replace/Keep. Declining closed the prompt without success feedback; the existing file hash stayed unchanged. | FR-6 |
| Request replacement again and choose “Replace” | The window reported success after publication. The destination changed from the header-only file to the complete two-record CSV and matched the independently inspected nonempty export byte-for-byte. The catalog hash stayed unchanged. | FR-6, FR-7 |

The [coverage report](./coverage.md) contains automated evidence for corrupt reads, failed writes, and preservation of pre-existing destination bytes under failure (FR-8). No product deviation was observed in the graphical flows above.

## Pending manual acceptance

- Open both exported files in **Microsoft Excel configured for Spanish regional settings**. Confirm eight separate English-headed columns, two ordered records in the nonempty file and no records in the empty file, the byte and binary size values, and Notes containing punctuation, quotes, Unicode, and line breaks in single cells. Confirm formula-like Title and Notes display literally and do not execute. This could not be verified with the installed software.
- The feature's Excel-specific completion criterion in the [specification](./spec.md) remains open until this check is performed. Passing CSV parsing and headless tests do not establish Excel's actual display or formula handling.
