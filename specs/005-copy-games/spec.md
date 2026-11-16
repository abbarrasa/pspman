# Spec 005 — Copy games to a PSP
Status: approved
Revision: 0.5

## Context and objective

Allow a collection owner to explicitly copy a catalogued game's decompressed ZIP/RAR contents to a connected PSP, with visible progress, overwrite consent, cancellation and clear outcome notifications. Reuse discovery, mounting and session associations from [Spec 004](../004-device-mounting/spec.md). Its outstanding native validation remains outstanding. Cancellation and failure retain all destination output, as explicitly requested during clarification.

## Users and actors

- Collection owner using the game catalogue.
- Connected source media and USB-connected PSP storage.
- Operating system providing device access, mounting authorization and available space.

## User stories

- US-1: As a collection owner, I want to copy a selected game to my chosen PSP without manually extracting its archive.
- US-2: As a collection owner, I want progress and outcome notifications so I know whether the copied game is complete or requires attention.
- US-3: As a collection owner, I want to stop a transfer and be warned that incomplete or incorrect destination output remains for me to handle.

## Definitions

- **Source archive:** the ZIP or RAR file at the selected source volume's current mount root plus the complete catalog Location, using Spec 004 path rules. Multipart archives include their associated parts.
- **Destination root:** the selected PSP's current filesystem root. The supported PSP has one volume; several PSP consoles still require per-game destination selection.
- **Game size:** catalog `SizeBytes` represents decompressed size. Actual decompressed size takes precedence for space checks and progress if they differ.
- **Destination directory:** uppercase `ISO` directly under the destination root (`/ISO`).
- **Operation output:** files/directories created or files overwritten by the current operation, including partially written files.
- **Retained output:** after cancellation, failure or confirmed closure, output remains as written, potentially incomplete or incorrect. The application does not delete it, restore originals or resume the transfer. The user must inspect and handle it.
- **Skipped entry:** an entry omitted because overwrite consent was declined or because its path is unsafe or it is a link.

## Functional requirements

- FR-1: When a game is selected in the catalogue, the system shall expose an explicit copy action for that game; selection alone shall not write game files.
- FR-2: When preparing a copy, the system shall use Spec 004 recognition, mounting, authorization and per-game PSP selection. If no PSP is connected or no destination is usable, the system shall notify the user and shall not write. If multiple PSPs require a choice, the system shall obtain it before writing and display the selected PSP and root.
- FR-3: When resolving the source, the system shall combine its selected volume's current root with the complete Location without modifying the catalogue. If the source is unavailable, the path is missing or the archive is unreadable, the system shall report the reason without silently selecting a different source.
- FR-4: Before destination writes, the system shall determine actual decompressed size and compare it with the PSP volume's available bytes. If actual size differs from SizeBytes, actual size shall govern without changing the catalogue. If free space is insufficient or cannot be determined, the system shall notify the user and shall not write; inaccurate SizeBytes alone shall not reject a copy that fits its actual size.
- FR-5: When copying supported ZIP/RAR contents, the system shall preserve relative directory structure and file contents under the destination directory, except for skipped entries under FR-6/FR-12. It shall create the directory if absent as part of the explicit copy action, and shall not convert game formats or filter by catalog Format.
- FR-6: When a destination file would be overwritten, the system shall request confirmation individually for that file. If declined, it shall skip that file and continue. If the destination changes after confirmation but before writing, it shall request confirmation again. The prompt shall explain that cancellation/failure leaves potentially incomplete or incorrect output and does not restore overwritten originals. No overwrite shall occur without consent.
- FR-7: When a game is sent for copying, the system shall display a copy screen with a table containing the game, destination, state and one combined progress bar for decompression and copying. Measurable progress shall use actual decompressed bytes rather than inaccurate SizeBytes. While totals are unavailable, it shall indicate preparation. Failed/cancelled operations shall not be presented as successfully complete.
- FR-8: When all non-skipped entries have been processed and output files closed, the system shall notify completion. If any entries were skipped, it shall display “Completed with skipped files”, including when every entry was skipped. Otherwise it shall report successful completion.
- FR-9: While preparation, password entry, overwrite consent or copying is pending, the system shall allow cancellation. If cancellation is accepted before completion, it shall stop processing, close operation files, retain destination output without cleanup or restoration, and report cancellation. The notification shall warn that retained output may be incomplete or incorrect and must be handled by the user, identifying the destination.
- FR-10: If source/destination loss, decompression failure or a write error interrupts a copy, the system shall stop processing, close operation files, retain destination output without cleanup or restoration and notify failure and its reason. It shall warn that output may be incomplete or incorrect and must be handled by the user, identifying the destination. It shall not redirect writes to a replacement device using the same path.
- FR-11: While one operation is preparing, awaiting input, transferring or stopping, the system shall prevent another copy from starting and explain that an operation is active. No queue or concurrent transfer shall be created in this iteration.
- FR-12: Before writing entries, the system shall skip unsafe paths, all archive links, and entries whose destination would escape the destination directory or write through a link outside it. It shall continue with other entries and include the skipped entries in FR-8's completion outcome. It shall never write archive contents outside the selected PSP's destination directory.
- FR-13: When an archive requires a password, the system shall request it from the user and use the entered value for extraction. If the password is incorrect, it shall allow retry or cancellation; cancelling/dismissing password entry shall cancel the operation under FR-9.
- FR-14: When an archive is multipart, the system shall decompress its parts in archive-defined order. If a part cannot be processed, it shall stop the game's copy and report failure under FR-10.
- FR-15: If an archive is empty, the system shall produce no destination copy and notify the reason. If archive corruption is detected before or during copying, the system shall stop the operation and report corruption under FR-10, retaining any output already written without cleanup or restoration. Corruption discovered in a later entry or part shall have the same failure outcome; a complete integrity-validation pass before destination writes is not required.
- FR-16: While a copy is active, if the selected game, selected source or selected PSP changes, or the user requests safe removal of a device used by the copy, the system shall fail the operation under FR-10. For requested safe removal, it shall stop the copy and close its operation files, then proceed with Spec 004 safe removal without requiring another request. Removal rejection/errors shall remain visible; no forced removal shall be used.
- FR-17: When the user requests normal application closure during a copy, the system shall request confirmation explaining that current output will remain and may be incomplete or incorrect. If accepted, it shall stop the current operation under FR-9, close its files and close the application without destination cleanup. If declined, the application and copy shall remain active. Files from previous completed copies shall not be deleted; any files already overwritten by the current operation remain in their current state without restoration.
- FR-18: When a later archive entry has the same destination path as an earlier entry, the system shall treat it as an overwrite under FR-6. Path equality, including case-only differences, shall follow the destination filesystem's semantics. If an entry has an incompatible destination filename or a file/directory conflict, the system shall fail the entire copy under FR-10 rather than silently rename or skip it.
- FR-19: If an encrypted entry or encrypted-header decoding error cannot be distinguished as an incorrect password or archive damage, the system shall stop decoding and destination writes, close current output files, retain everything already written and display "Incorrect password or damaged archive" with explicit password-retry and cancellation choices. It shall not assert a definite cause or resume writing automatically. A user-selected password retry shall follow FR-13 and normal overwrite consent under FR-6; cancellation shall follow FR-9. For this indeterminate case, FR-19 qualifies FR-10/FR-15: the operation waits for that decision instead of immediately reporting confirmed corruption as a terminal failure. Confirmed corruption and unrelated I/O/device failures retain their existing failure outcomes.

## Non-functional requirements

- NFR-1: The feature shall support Linux and Windows and preserve Spec 004 identity, source selection and no-fallback rules. macOS is outside this iteration.
- NFR-2: During archive inspection, decompression, copying and stopping, the application shall remain interactive, display operation state and accept cancellation before completion. Functional verification shall exercise pending I/O and cancellation without blocking catalogue viewing; changing the selected game still has FR-16's explicit failure effect.
- NFR-3: Product text and functional documentation shall be in English. Filesystem roots shall follow host-platform syntax without changing catalog Location.
- NFR-4: The operation shall not modify source archive parts or catalog records. Automated verification shall compare their bytes/state before and after success, cancellation and failure.

## Edge cases

- Source selection and ambiguous sources follow Spec 004; a remembered path alone does not establish device identity (FR-2, FR-3).
- Free bytes exactly equal to actual decompressed size pass the byte comparison; filesystem overhead or competing writes can still produce a reported failure with retained output (FR-4, FR-10).
- Nested directories and multiple files remain under `/ISO`, except skipped entries (FR-5, FR-12).
- Existing files not overwritten remain unchanged. Previously overwritten files, including those from earlier game copies, remain as written if the current operation stops; originals are not restored (FR-6, FR-9, FR-10, FR-17).
- Unsafe entries/links and declined overwrites yield completion with skipped files if all other entries finish, even if all entries are skipped (FR-8, FR-12).
- Duplicate paths are processed as overwrites; case sensitivity follows the actual destination filesystem, not an assumed host-platform rule (FR-18).
- Destination file/directory conflicts and incompatible names fail the copy; already-written output remains for the user to handle (FR-10, FR-18).
- Corruption detected before any output is written leaves no copied output; corruption discovered after some writes retains those files, including partial files, and reports failure rather than completion (FR-10, FR-15).
- An indeterminate encrypted decoding error stops I/O and retains output while the user chooses password retry or cancellation; it is not labelled confirmed corruption or a confirmed wrong password (FR-19).
- Cancellation is accepted only before the operation becomes terminal; a terminal result is not reversed by a late request. Repeated stop requests shall not restart processing or create another copy (FR-9–FR-11).
- Physical disconnection and native authorization prompts retain Spec 004 platform limitations. Forced process termination is not normal application closure evidence.

## Out of scope

- Concurrent copies, copy queues, batch selection and automatic retry/resume of failed copies. Password retry is explicitly included.
- Automatic destination cleanup, restoration of overwritten originals, persistent transfer history and crash recovery.
- Game conversion, source archive modification, media formatting or repair.
- New device providers, multi-volume PSP destinations or claiming Spec 004 native validation has passed.

## Acceptance criteria

- AC-1 (FR-1, FR-2): Explicit copy obtains a usable PSP destination or reports absence; selecting a game alone writes nothing. Multiple PSP choices preserve the selected device and display its root.
- AC-2 (FR-3, NFR-4): Source resolution retains the selected root and full Location. Missing/unreadable archives report errors without fallback, source changes or catalogue changes.
- AC-3 (FR-4): When actual size differs from SizeBytes, actual size governs space checks. Insufficient/unknown free space prevents writes; sufficient actual space is not rejected solely due to an inaccurate larger SizeBytes.
- AC-4 (FR-5, FR-8): Valid ZIP/RAR fixtures with nested directories produce matching file bytes/structure under `/ISO`, except explicitly skipped entries, and files are closed before completion notification.
- AC-5 (FR-6): Every overwrite requests individual consent; declined files remain untouched. Modification between consent and writing requests renewed consent. The prompt explains retained incomplete/incorrect output and no restoration.
- AC-6 (FR-7, NFR-2): Pending preparation and transfer display the game, destination, state and one combined decompression/copy bar based on actual size while the UI remains interactive.
- AC-7 (FR-9, NFR-4): Cancellation during preparation, input or copying stops processing, closes operation files and warns about retained output without deleting files/directories or restoring originals. Source/catalogue remain unchanged.
- AC-8 (FR-10): Source/destination disconnection, extraction failure and write failure produce a failure reason and destination warning; output remains, no destination cleanup occurs, and writes are not redirected to a replacement device.
- AC-9 (FR-11): A second copy cannot start while one is active or stopping; no queue or parallel transfer is created.
- AC-10 (FR-12, FR-8): Unsafe paths and links are skipped without outside writes; remaining valid entries continue and completion says “Completed with skipped files”, including an all-skipped archive.
- AC-11 (NFR-1, NFR-3): Linux/Windows evidence covers platform roots, association and no-fallback behavior; UI and documentation use English.
- AC-12 (FR-13): Password-protected fixtures prompt for a password; incorrect input allows retry or cancellation, valid input permits extraction, and dismissal cancels with FR-9 retention behavior.
- AC-13 (FR-14): Complete multipart fixtures extract in correct order; missing/invalid parts fail with notification and retention of any output already written.
- AC-14 (FR-15): Empty archives copy nothing and report the reason. Corruption detected before writing reports failure without copied output. Corruption detected during copying, including in a later entry/part, stops further copying, closes operation files, retains all output already written and reports corruption with FR-10's incomplete/incorrect-output warning. No deletion or original-file restoration occurs.
- AC-15 (FR-16): Game/source/PSP selection changes or removal requests fail the active copy and warn about retained output. Requested removal proceeds only after the copy stops and releases its files, with normal removal errors reported.
- AC-16 (FR-17): Declining close confirmation keeps the application/copy active. Accepting stops the current copy, closes operation files and exits without destination cleanup; previously completed copies are not deleted as separate operations.
- AC-17 (FR-18): Duplicate paths request overwrite consent; case-only collisions follow destination semantics. File/directory conflicts and incompatible names fail the whole operation with retained output and notification.
- AC-18 (FR-8, FR-9, FR-11): Some or all skipped entries produce the specified skipped-files outcome. A cancellation accepted before terminal completion stops the operation; late/repeated stop requests do not reverse a terminal result or restart processing.
- AC-19 (FR-19, FR-6, FR-9, FR-10, FR-13, FR-15): Given either a wrong password or damaged encrypted input that produces an indeterminate decoding error, the system stops decoding/writing, closes current outputs, retains written bytes and displays "Incorrect password or damaged archive" without claiming a definite cause. No further writes occur until explicit retry; retry observes normal overwrite confirmation and cancellation terminates under FR-9. Confirmed corruption and ordinary source/destination failures still fail under FR-10/FR-15 without being misclassified as this password decision.

## Completion criteria

- Verification shall cover indeterminate encrypted errors from both wrong-password and damaged-input cases, retry/overwrite consent, cancellation and retained output (FR-19, AC-19).
- Every FR/NFR and acceptance criterion has traceable implementation and verification evidence, including ZIP/RAR, password retry/cancel, multipart input, overwrite/reconfirmation, skips, actual size/progress, cancellation/failure retention and confirmed close/removal.
- Unit, archive/filesystem integration and affected UI functional tests pass; coverage is measured and significant gaps justified under the constitution. No numeric threshold has been approved; coverage targets are to be established with feasible verification.
- `dotnet build PSPMan.slnx` and `dotnet test PSPMan.slnx` pass. UI startup is checked when a graphical environment is available, using controlled media/device conditions.
- All relevant physical/native copy tests on Linux and Windows are mandatory. If hardware/environment is unavailable, document unexecuted tests, blockers and future environment/procedure needs as for Spec 004. Deferred tests do not pass and leave physical validation incomplete. Previous device evidence is not new copy acceptance.
- SDD code review passes and the user accepts validation before this specification is marked implemented.

## Open questions

None. The user explicitly approved the displayed revision 0.5.

## Approval and clarification record

- The user approved initial scope/specification drafting and then authorized clarification. Initial scope included `/ISO`, decompressed SizeBytes, full structure, overwrite consent and one active copy; concurrency remains deferred.
- Revisions 0.1/0.2 considered deleting output on cancellation/failure, without restoring overwritten originals. Those deletion rules were explicitly superseded by the latest user decision: retain all output on cancellation, failure and closure; warn that the user must handle incorrect/incomplete content.
- The user clarified one PSP volume; password/multipart support; actual-size precedence; per-file overwrite confirmation and skip on refusal; failure on selection changes/removal requests; confirmed-close cancellation; mandatory physical tests with documented blockers.
- The latest response also specifies renewed consent on destination changes, duplicate entries as overwrites, destination filesystem case semantics, failure on file/directory conflicts or incompatible names, unsafe/link skips, password retry/cancel, completion with skips even if all omitted, one combined progress bar and continuing requested safe removal.
- The user's final clarification explicitly resolves late corruption as an ordinary failure: stop copying and retain what has been written. This supersedes the previous zero-write-on-corruption requirement, while empty archives still copy nothing (FR-15, AC-14).
- QA re-review of revision 0.4: skipped-content behavior, overwrite confirmation, actual size, password/multipart handling, retained output, safe removal and closure have matching acceptance criteria. No unresolved blocking ambiguity, contradiction or constitution conflict was found. Physical-test blockers remain a validation limitation to record, not passing evidence.
- The user explicitly approved revision 0.4 with “Apruebo la revisión”. Its status is approved; no requirements changed after presentation. Implementation planning awaits separate authorization; no tasks, code or validation are authorized. There are no downstream plan/task documents yet.
- During the later backend review, the user accepted the proposed indeterminate encrypted-error message, retained-output behavior and explicit password retry/cancellation with “Sí, acepto”. Revision 0.5 incorporates that decision in FR-19/AC-19 and their edge/completion criteria; revision 0.4 approval remains historical. No other requirement is changed. QA review found no remaining blocking ambiguity or constitution conflict in this change.
- The user explicitly approved displayed specification revision 0.5 with “apruebo la revisión”; no requirement changed after its presentation. Status is approved. The already authorized plan review can continue; the revised plan and affected tasks require approval before dependent implementation. No replacement-backend implementation or T02 work is authorized by this specification approval.
