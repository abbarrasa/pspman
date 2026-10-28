# Game Catalog CSV Export Specification

## Context and objective

Collection owners need a portable copy of their game catalog that they can inspect and share in Excel without reconnecting removable media or changing the catalog. Exporting every saved game record to a CSV file makes the collection usable outside PSPMan while preserving field values, including free-form notes.

## Users and actors

- A collection owner who exports and opens the catalog in Excel configured for Spanish regional settings.

## User stories

- H1: As a collection owner, I want to export all saved games so I can review the collection in Excel.
- H2: As a collection owner, I want notes and other text to remain in their original cells so I can trust the exported data.
- H3: As a collection owner, I want to choose the export destination and approve replacement of an existing file so I remain in control of my files.

## Functional requirements and acceptance criteria

- FR-1: WHEN the user requests a catalog export, THE SYSTEM SHALL ask where to save the CSV and under what name; WHEN the user cancels this choice, THE SYSTEM SHALL leave the catalog and destination files unchanged.
- FR-2: WHEN the user confirms a destination, THE SYSTEM SHALL export every saved game record exactly once, regardless of selection or media availability, in catalog display order. THE SYSTEM SHALL include a header row and these columns in this order: Title, Medium, DVD Number, Location, Format, Decompressed Size (bytes), Approximate Size (binary units), and Notes. THE SYSTEM SHALL NOT include internal identifiers.
- FR-3: THE SYSTEM SHALL produce a CSV that opens as separate columns in Excel configured for Spanish regional settings: UTF-8 with a byte-order mark, semicolon-separated fields, and English column headings. THE SYSTEM SHALL represent decompressed size as an ungrouped integer byte count and the approximate size as the same human-readable binary-unit value shown in the catalog; a non-DVD record SHALL have an empty DVD Number cell.
- FR-4: WHEN a field contains a semicolon, double quote, or line break, THE SYSTEM SHALL preserve its complete value in one Excel cell by quoting that field and doubling embedded double quotes. Other punctuation, including commas and colons, SHALL be preserved. Empty Notes SHALL produce an empty cell.
- FR-5: WHEN a text field could be interpreted by Excel as a formula because its first non-whitespace character is `=`, `+`, `-`, or `@`, THE SYSTEM SHALL make Excel display it as literal text without executing a formula or hiding its original visible content.
- FR-6: IF the chosen destination already contains a file, THEN THE SYSTEM SHALL request confirmation before replacing it; IF the user declines, THEN THE SYSTEM SHALL leave that file unchanged and SHALL NOT report a successful export.
- FR-7: WHEN an export completes successfully, THE SYSTEM SHALL inform the user that the chosen destination contains the complete catalog; THE SYSTEM SHALL NOT alter catalog records or game files.
- FR-8: IF the catalog cannot be read, its data are corrupt, or the destination cannot be written completely, THEN THE SYSTEM SHALL explain the failure, SHALL NOT report success, and SHALL leave any pre-existing destination file unchanged or leave no incomplete new file as the exported result.
- FR-9: WHEN the catalog is empty, THE SYSTEM SHALL allow export and produce only the header row.

## Non-functional requirements

- Export SHALL work on supported desktop platforms without requiring removable media to be connected.
- User-facing text and CSV headings SHALL be in English; the CSV format SHALL remain compatible with Excel configured for Spanish regional settings.
- Opening the CSV in Excel SHALL NOT execute formulas supplied through catalog text.

## Edge cases

- Notes and other text may contain semicolons, commas, colons, double quotes, and line breaks; each exported record must still occupy one logical CSV row, and each field one Excel cell.
- An empty catalog exports a header without data rows. Empty Notes and non-DVD DVD Numbers export as empty cells.
- Distinct records with identical titles or similar locations remain distinct exported rows; export does not deduplicate or filter records.
- A disconnected medium or missing game file does not prevent export. Export never reads or modifies a game file.
- Cancelling destination selection or declining replacement changes no destination file. Read errors, corruption, permission errors, insufficient space, and interrupted writes are reported without a partial file being presented as complete.
- Text beginning with formula-triggering characters, including after leading whitespace, opens as literal text in Excel.

## Out of scope

- Importing CSV data or synchronizing changes made in Excel back to PSPMan.
- Exporting only selected, filtered, or manually ordered records; choosing columns or changing the CSV dialect.
- Exporting game-file contents, scanning removable media, or calculating decompressed size from files.
- Repairing corrupt catalog data or converting exports into spreadsheet workbook formats.

## Completion criteria

- Every FR has passing tests at the applicable unit, integration, or functional level; coverage is measured and significant gaps are justified.
- A manual demonstration opens an exported non-empty catalog in Spanish-configured Excel and checks headings, sizes, Notes with semicolons, quotes and line breaks, and literal formula-like text. It also checks empty export, cancellation, and replacement confirmation.
- Failed export and corrupt-catalog scenarios leave existing destination content unchanged in automated acceptance tests.

## Open questions

- None.
