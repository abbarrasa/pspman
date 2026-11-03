# Game Catalog CSV Import Specification

## Context and objective

Collection owners need to bring game records into PSPMan from a CSV, including copies edited in Excel, without re-entering each record. Importing creates missing records and updates existing ones by their catalog identity, so a portable catalog can be corrected and brought back while the owner sees what will change and which rows could not be used.

## Users and actors

- A collection owner who imports game records from a CSV, possibly edited in Excel.

## User stories

- H1: As a collection owner, I want to import a CSV so I can add multiple games without entering them individually.
- H2: As a collection owner, I want matching catalog records updated so I can bring edited information back into PSPMan.
- H3: As a collection owner, I want to review the proposed changes and row errors before confirming so I can avoid unintended changes.
- H4: As a collection owner, I want valid rows to succeed despite other invalid rows so I can use as much of the file as possible.
- H5: As a collection owner, I want a CSV template with permitted values so I can prepare a file that PSPMan can import.

## Functional requirements and acceptance criteria

- FR-1: WHEN the user requests import, THE SYSTEM SHALL ask them to choose a CSV; WHEN they cancel, THE SYSTEM SHALL leave the catalog unchanged.
- FR-2: WHEN a CSV is chosen, THE SYSTEM SHALL read its logical records in order, including records whose quoted Notes contain line breaks. THE SYSTEM SHALL require these seven headings in any order: Title, Medium, DvdNumber, Location, Format, SizeBytes, and Notes. Heading matching SHALL ignore letter case; heading cells SHALL contain no whitespace characters. WHEN a heading occurs more than once under that matching rule, THE SYSTEM SHALL use the value from its last column for each row. THE SYSTEM SHALL ignore additional columns and internal identifiers.
- FR-3: THE SYSTEM SHALL accept semicolon-separated UTF-8 CSV with an optional byte-order mark, quoted fields with doubled quotes, and line breaks within quoted fields. THE SYSTEM SHALL preserve field values containing punctuation and line breaks. WHEN a text value starts with a tab and its first non-whitespace character after that tab is `=`, `+`, `-`, or `@`, THE SYSTEM SHALL remove exactly that first tab before validating or saving the value, even if it was intentionally entered in the CSV. THE SYSTEM SHALL use SizeBytes as the decompressed integer byte count.
- FR-4: WHEN a row contains a valid game record whose Medium, case-sensitive Location, and DVD Number for DVD match an existing record, THE SYSTEM SHALL propose replacing that record's other editable values with the row's values, including clearing Notes when its cell is empty, while preserving the record's internal identity. IF all field values already match that existing record, treating absent and empty Notes as equivalent, THEN THE SYSTEM SHALL make no change to it. WHEN no record matches, THE SYSTEM SHALL propose creating one. Title and Format SHALL NOT affect matching.
- FR-5: WHEN multiple valid rows have the same catalog identity, THE SYSTEM SHALL apply the last valid row's values for that identity; an invalid later row SHALL NOT replace an earlier valid row.
- FR-6: IF a nonempty row has fewer cells than the header, lacks a required value for that game, or violates the game-record rules for allowed values, DVD Number, Location, positive decompressed byte size, or the 2,500 UTF-16-code-unit Notes limit, THEN THE SYSTEM SHALL exclude that row and identify its source row and reason in the import report. THE SYSTEM SHALL ignore cells beyond the header's cell count and ignore lines containing no CSV fields or only spaces and tabs outside quoted fields. Medium SHALL accept Dvd, Usb, or MemoryCard; Format SHALL accept ISO or CHD. Matching these values SHALL ignore letter case, but values containing whitespace characters SHALL be invalid. THE SYSTEM SHALL continue evaluating other readable rows.
- FR-7: WHEN the file has been evaluated, THE SYSTEM SHALL show a summary of new games, modified games, and erroneous rows, including row-specific errors, and ask for confirmation before changing the catalog. THE SYSTEM SHALL NOT count an unchanged matching record as new, modified, or erroneous. WHEN the user declines, THE SYSTEM SHALL leave the catalog unchanged.
- FR-8: WHEN the user confirms, THE SYSTEM SHALL save the valid proposed records and report how many were created, updated, or failed. IF a proposed record cannot be saved, THEN THE SYSTEM SHALL report its failure and SHALL NOT present that record as saved; other valid proposed records SHALL remain eligible for import.
- FR-9: IF the file cannot be opened, any required heading is missing, a heading contains whitespace, its structure cannot be interpreted sufficiently to identify records, or a read error occurs before the preview is shown, THEN THE SYSTEM SHALL fail the entire preview, explain the failure, and SHALL NOT change the catalog. IF a read error occurs after the preview is shown and import has begun, THEN THE SYSTEM SHALL report the error, stop importing, and retain game changes already saved. IF catalog data are corrupt, THEN THE SYSTEM SHALL inform the user and SHALL NOT repair or overwrite them.
- FR-10: WHILE a removable medium is unavailable or a referenced game file is missing, THE SYSTEM SHALL still allow its valid catalog record to be imported. THE SYSTEM SHALL NOT read, modify, or delete game files.
- FR-11: WHEN the CSV contains a valid header but no game rows, THE SYSTEM SHALL report zero proposed changes and leave the catalog unchanged.
- FR-12: WHEN the user confirms the preview, THE SYSTEM SHALL import only from the unchanged CSV that produced it. IF the CSV has changed or can no longer be verified before the first game is saved, THEN THE SYSTEM SHALL report the error and leave the catalog unchanged. IF the CSV changes after saving has begun, THEN THE SYSTEM SHALL stop importing, report the error, and retain game changes already saved.
- FR-13: WHEN the user requests guidance for preparing an import, THE SYSTEM SHALL provide an illustrative CSV template with the seven required headings and entries showing every permitted Medium value (Dvd, Usb, MemoryCard) and Format value (ISO, CHD). THE SYSTEM SHALL leave other required game fields unfilled in those entries, so importing the unedited template SHALL create or modify no games. Providing the template SHALL NOT change the catalog.

## Non-functional requirements

- Import SHALL behave consistently on supported desktop platforms; Location matching SHALL remain case-sensitive on every platform.
- User-facing text and CSV headings SHALL be in English. The accepted CSV format SHALL support files exported by PSPMan and then edited in Excel configured for Spanish regional settings.
- Import SHALL impose no fixed CSV file-size limit; the amount that can be processed depends on the resources of the machine running PSPMan.
- Import SHALL handle quoted multiline fields as one logical record and identify rejected records unambiguously in the report.

## Edge cases

- An empty file, a missing required heading, unrecoverable quoting, or a read error before the preview fails the entire preview without catalog changes. A read error after the preview stops import and leaves previously saved games in place. Repeated headings use the value in the last matching column, even when an earlier matching column has a valid value.
- Blank lines and lines containing only spaces and tabs outside quoted fields are ignored; line breaks inside quoted fields remain part of the field. Rows shorter than the header are reported as invalid; cells beyond the header are ignored. Other row-level invalid values are reported without blocking readable rows. A malformed row that prevents identifying where the next logical record begins may prevent evaluation of subsequent rows.
- A heading containing any whitespace character fails the preview, including a heading in an otherwise ignored extra column. Medium and Format values containing any whitespace character fail only their rows; letter case does not affect matching.
- Empty Notes clear existing Notes; an invalid later duplicate does not override an earlier valid row. Only the last valid row for an identity is proposed.
- A final valid row identical to its matching catalog record produces no write and contributes to none of the preview's new, modified, or erroneous counts. Absent and empty Notes are equivalent for this comparison.
- DVD records require a positive DVD Number; zero means absent and is invalid for DVD. Non-DVD records have no DVD Number. Location is a complete medium-rooted path with `/` separators and is compared case-sensitively.
- SizeBytes is the positive integer decompressed byte count. Quoted semicolons, quotes, Unicode text, and line breaks in Notes remain part of their field.
- A formula-like text value with one or more leading tabs loses exactly its first tab on import; all remaining whitespace is preserved. Text without both a leading tab and a formula-like first non-whitespace character keeps its leading whitespace.
- Changes to the selected CSV after preview invalidate it. Changes detected before saving begins leave the catalog unchanged; changes detected during import stop further saves and leave earlier saved games in place.
- The unedited template shows all permitted Medium and Format values but contains no valid game records; it cannot create illustrative games by accident.
- Cancelled selection or confirmation changes nothing. An unavailable medium or missing game file does not block import. Catalog corruption is reported without recovery.

## Out of scope

- Importing game-file contents, scanning media, copying or deploying games, and verifying that a referenced file exists.
- Deleting catalog records absent from the CSV, merging only nonempty fields, or synchronizing changes automatically.
- Accepting spreadsheet workbook formats or arbitrary CSV dialects and translated column headings.
- Automatically repairing corrupt catalog data or deriving decompressed size from game files.

## Completion criteria

- Every FR has passing tests at the applicable unit, integration, or functional level; code coverage is measured in a report, and significant gaps are justified.
- A manual demonstration checks every permitted Medium and Format value in the template and verifies that importing it unchanged creates no games. It also covers reordered and extra columns, case-insensitive headings and choices, whitespace rejection in headings and Medium/Format, creation, update with empty Notes, an identical matching record that is neither counted nor written, duplicate identities, short and whitespace-only rows, formula-protected text, preview cancellation, confirmed import, a CSV changed after preview, a read error after preview, and an unavailable medium.
- Open questions affecting acceptance behavior are resolved before implementation.

## Open questions

- None.
