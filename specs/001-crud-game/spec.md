# Game Record CRUD Specification

## Context and objective

PSPMan needs a catalog of game records so collectors can find and maintain information about games stored on removable media without having to connect each medium. This feature lets a user create, view, edit, and remove catalog records while leaving the game files untouched. Recording decompressed size will support future checks of available space on a PSP.

## Users and actors

- A collection owner who maintains and consults the game catalog.

## User stories

- H1: As a collection owner, I want to add a game record so I can locate a stored game later.
- H2: As a collection owner, I want to browse and view game records so I can identify where a game is stored.
- H3: As a collection owner, I want to correct a record so the catalog stays accurate.
- H4: As a collection owner, I want to remove an obsolete record without deleting the game file.

## Functional requirements and acceptance criteria

- FR-1: WHEN the user opens the catalog, THE SYSTEM SHALL list saved game records; WHEN no records exist, THE SYSTEM SHALL show an empty state.
- FR-2: THE SYSTEM SHALL provide these record fields: required Title, Medium, Location, Format, and Size; DVD Number required only for DVD records; optional Notes. Medium SHALL offer DVD, USB, and Memory Card; Format SHALL offer ISO and CHD. DVD Number SHALL be a positive integer or null, with zero treated as null.
- FR-3: WHEN the user views a record, THE SYSTEM SHALL show its saved field values. Location SHALL be a complete path from the medium's root, without the host's mount point (for example, `/DevHook/GAMES/Key of Heaven.rar`). Size SHALL be the decompressed file size in bytes entered by the user.
- FR-4: IF a required field is blank (including a Title made only of whitespace), Size is not a positive byte count, DVD Number is missing for a DVD or present for another medium, Location is not well formed, Notes exceeds 2,500 UTF-16 code units, or Medium or Format is outside its allowed values, THEN THE SYSTEM SHALL explain the invalid field and SHALL NOT save the record.
- FR-5: WHEN the user submits a valid new record, THE SYSTEM SHALL save it and make it available in the catalog.
- FR-6: IF another record has the same case-sensitive Location, the same Medium, and the same DVD Number for a DVD, THEN THE SYSTEM SHALL reject creation or editing of the duplicate, explain the conflict, and leave existing records unchanged. Title SHALL NOT affect duplicate detection.
- FR-7: WHEN the user saves valid edits to a record, THE SYSTEM SHALL show the updated values in the catalog without changing the game file.
- FR-8: WHEN the user requests deletion, THE SYSTEM SHALL ask for confirmation. WHEN the user confirms, THE SYSTEM SHALL remove only that catalog record and SHALL NOT delete or modify the game file; WHEN the user cancels, THE SYSTEM SHALL leave the record unchanged.
- FR-9: WHILE a medium is disconnected or the file at Location cannot be found, THE SYSTEM SHALL still allow the user to create, view, and edit its catalog record.
- FR-10: IF a catalog operation cannot be completed, THEN THE SYSTEM SHALL report the failure and SHALL NOT present the requested change as saved.
- FR-11: IF catalog data cannot be read because they are corrupt, THEN THE SYSTEM SHALL inform the user and SHALL NOT attempt automatic recovery or overwrite the existing data.

## Non-functional requirements

- The catalog workflow SHALL behave consistently on supported desktop platforms, including when removable media are unavailable.
- User-facing text for this feature SHALL be in English.

## Edge cases

- An empty catalog displays an empty state; missing required values or unsupported choices prevent saving. Whitespace includes every whitespace character, not only ordinary spaces.
- A duplicate is determined by Location and Medium, plus DVD Number for DVD records, regardless of Title or Format. Location comparison is case-sensitive on every platform; paths that differ only in letter case are distinct. The rule applies to both new and edited records.
- DVD Number is required for DVD records and absent for USB and Memory Card records; entering zero makes it null, so it does not satisfy the DVD requirement. Size is a positive decompressed byte count entered by the user, even when the medium is unavailable.
- Location must start at the medium's root, use `/` separators, include a file name, and have no empty, `.` or `..` path segments, backslashes, or control characters; malformed paths prevent saving. Validation is syntactic and does not infer whether a directory name resembles a host mount point. Notes may be empty but cannot exceed 2,500 UTF-16 code units (the length of a .NET string).
- Records remain usable while their medium is disconnected or their file is missing.
- Cancelling deletion leaves the record and game file unchanged. A failed save or deletion must be reported rather than shown as successful.
- Corrupt catalog data are reported without automatic recovery or overwriting. The standalone application does not support concurrent operations on the same record by multiple users.

## Out of scope

- Scanning or importing games automatically; copying, deploying, extracting, deleting, or modifying game files.
- Checking available space on a PSP or deciding whether a game can be loaded onto it.
- Obtaining the decompressed size automatically from the medium or repairing corrupt catalog data.
- Verifying that a medium is connected or a file exists as a condition for saving a record.
- Managing media or physical storage locations beyond the fields in a game record.

## Completion criteria

- Every functional requirement has passing tests at the applicable unit, integration, or functional level; code coverage is measured in a report, and significant gaps are identified and justified.
- A manual demonstration covers create, list/view, edit, confirmed and cancelled deletion, duplicate rejection, and editing while the medium is unavailable.
- Open questions that affect acceptance behavior are resolved before implementation starts.

## Open questions

- None.
