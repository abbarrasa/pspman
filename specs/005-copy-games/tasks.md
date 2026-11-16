# Tasks 005 — Copy games to a PSP
Status: approved
Revision: 0.2
Specification: [spec.md](./spec.md), approved revision 0.5
Plan: [plan.md](./plan.md), approved revision 0.2

## Execution rules and evidence

The user approved tasks 0.1 and authorized its managed-backend T01, then approved specification 0.5 and plan 0.2. The user now explicitly approved tasks 0.2 and authorized resuming native T01. This revision preserves task identifiers and historical evidence while revising the native-backend and FR-19 scope. Execute only one explicitly authorized task, report its evidence and stop. Approval of the list does not authorize every task. Dependencies must pass before dependent work begins. No task is complete merely because its implementation compiles.

The ten increments below separate archive compatibility, pure rules, adapters, orchestration and UI integration. The SDD 20–30 minute target is a decomposition guide, not a promised duration: native confinement, archive variants and lifecycle races may take longer. If an increment requires materially broader work, stop and propose a revised decomposition rather than silently expanding its scope or reducing the approved requirements.

Before implementation, run `git status --short --branch`. Start each feature task at local dev on `feature/005_TNN_<brief-kebab-case-description>`; preserve pre-existing changes. Do not stash, restore, commit, merge or push. Stop if switching is unsafe. The existing local deletions are outside these tasks.

For each task: write meaningful behavior tests first, confirm failure due to the absent behavior, implement the smallest approved change, run relevant checks plus required repository build/regression, inspect the diff, and record commands, counts, platform skips and limitations here and in MEMORY.md. Existing passing evidence may satisfy a behavior only when its scope is explicitly documented. Read the run-tests skill before executing .NET tests. Do not launch ordinary device discovery against arbitrary hardware.

Native/hardware checks unavailable in the current environment remain mandatory and unexecuted; record their exact blockers and procedures in pending-validation.md. A task whose explicit completion conditions require those checks remains open until they pass. Completing automated enabling tasks does not establish physical acceptance. Formal SDD validation/code review is a separate phase requiring authorization after implementation.

## Tasks

Execution exception explicitly authorized by the user: “tendría que preparar un entorno Windows. Deja pendiente estas pruebas y pasa a la siguiente tarea”. Windows T01 checks remain mandatory, unexecuted and open; the user authorizes T02's pure contracts/rules to proceed using the passing Linux evidence. This does not complete T01, waive Windows/physical acceptance or authorize T03. Record this exception rather than treating pending Windows results as passes.

- [ ] **T01. Prove the pinned native archive bridge compatibility gate.** FR-5, FR-13, FR-14, FR-15, FR-19, NFR-1, NFR-4
  - Depends on: none.
  - Work: Build the minimal C ABI bridge under native/PSPMan.ArchiveBridge against the full official 7-Zip 26.06 engine, with pinned source/hash/licence provenance and a test-local managed probe; production Core contracts and adapter belong to T02/T03. Reuse licensed fixtures and preserve the old managed-backend report/tests as historical characterization. Exercise plain/encrypted ZIP, split ZIP, RAR4/RAR5, solid RAR, encrypted headers/data, modern/legacy multipart, non-first-part discovery, bad password, missing parts, empty input and early/late corruption. Prove indexed metadata, duplicate identity, controlled per-index output/skip, caller-owned read/seek/part streams, decoded/output progress, cancellation and callback error propagation. The engine must never open destination paths. Cover definite errors and FR-19 indeterminate encrypted errors without asserting a false cause. Prove ABI ownership, Unicode and stream/delegate lifetime; identify all supported publish targets and toolchain/runtime prerequisites. Do not mount devices or wire a copy command.
  - Done when: Actual bridge/API tests, rather than CLI reference runs, demonstrate fixture metadata/order, expected payload hashes, skips, cancellation and closed source/output handles. The encrypted multipart RAR and encrypted LZMA ZIP previously rejected by SharpCompress pass; indistinguishable password/damage errors map to FR-19. Bridge version/ABI mismatch is detected. Record reproducible native build/test commands and audit/licence/source evidence. Repository build/full regression pass. Required Linux/Windows native gate cases must pass; unavailable cases are explicitly pending and leave T01 open. Any blocking API gap needs an approved correction. No proprietary games or runtime archive-generation utility is required by tests.
  - Evidence: in progress, **gate blocked; not complete**. Added pinned package, 33 licensed/hash-manifested fixtures and 42 characterization cases; see [T01 compatibility report](./archive-compatibility.md). Initial 14 RED cases failed for the absent probe. Final characterization passed 42/42, but encrypted multipart RAR fails through both async/sync readers, encrypted LZMA ZIP is rejected, and RAR4 password-versus-corruption classification remains ambiguous. Known-rejection assertions are not support acceptance. Build passed with zero warnings/errors; full regression passed 1475 with four Windows skips (618 Core / 545 Infraestructure / 312 Desktop). Advisory audit reported no known solution vulnerabilities. No PSP I/O/UI integration or T02 work. This is historical managed-backend evidence; specification 0.5, plan/tasks 0.2 and native T01 resumption are now explicitly approved.
  - Native revision 0.2 evidence: resumed on the existing branch; full pinned 7-Zip 26.06 engine, minimal C ABI bridge, test-local managed probe, build scripts/notices and native concurrency harness implemented. Initial 16 RED rows failed for the absent probe. Final Linux native API passed 49 cases through callbacks, including previously rejected encrypted multipart RAR/encrypted LZMA ZIP; harness passed Unicode and 2,000 concurrent serialized progress calls. Build passed with zero warnings/errors; full regression passed 1524 with four existing Windows skips (618/594/312). Source hashes and handle closure checked on success/cancel/failure. See [native gate report](./native-archive-compatibility.md). **T01 remains open**: mandatory native Windows build/API/harness/regression not executed; [restart procedures](./pending-validation.md) recorded. No PSP I/O, production adapter or T02 work.

- [x] **T02. Define transfer contracts and pure rules.** FR-3, FR-4, FR-7, FR-8, FR-9, FR-10, FR-11, FR-12, FR-18, FR-19, NFR-3, NFR-4
  - Depends on: T01; its Windows execution is deferred for T02 only by the explicit user instruction above.
  - Work: Add planned Core Transfers models, archive/filesystem/interaction contracts and GameCopyRules. Define operation/prompt identifiers, captured game/device/volume evidence, typed errors, states, skip reasons and terminal outcomes. Test unsafe relative paths/links, actual-size checked totals, zero/overflow conditions, destination-supplied case facts, combined progress and terminal transitions. The filesystem contract exposes conditional create/overwrite and stream ownership, with no destination deletion/restoration. Passwords are not snapshot/result fields. Preserve ordered duplicate entries.
  - Done when: Focused rule/contract tests pass for safe/unsafe paths, actual-size mismatch, zero and overflow, skipped/all-skipped completion, progress bounds, stale prompt IDs and completion/cancel races. Core has no SharpCompress, UI, persistence or native references; existing architecture guards pass. Repository build/full regression pass.
  - Revision 0.2 scope: native-neutral ordered extraction-target callbacks replace pull-entry-stream assumptions. Add typed definite/indeterminate encrypted errors and FR-19 retry/cancel states, prompt identity and retained-output transitions. Test these without native references; passwords remain transient.
  - Evidence: complete on feature/005_T02_transfer-contracts-rules, under the explicit T01 Windows dependency exception. Added Core models, indexed archive/source/target callbacks, conditional filesystem and interaction contracts, pure rules and atomic terminal arbitration. Initial 23 RED cases failed for missing behavior; final 30 cases pass, including close-failure priority, native name/case facts, overflow/zero/skips, monotonic progress, stale prompts and 100 stop/completion races. `dotnet build PSPMan.slnx` passed with zero warnings/errors; `PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --logger 'trx;LogFileName=t02-regression.trx'` passed 1554 with four existing Windows skips (648/594/312), including all 49 Linux native archive cases and existing architecture checks. No Core package/project change or production adapter/UI/PSP I/O. See [T02 evidence](./core-transfer-contracts.md). Pre-existing changes preserved; no commits/push. T01 Windows tests remain mandatory and pending; T03 not started.

- [ ] **T03. Implement ordered ZIP/RAR archive sessions.** FR-3, FR-4, FR-5, FR-13, FR-14, FR-15, FR-19, NFR-1, NFR-2, NFR-4
  - Depends on: T01, T02.
  - Work: Implement SevenZipGameArchiveReader, SevenZipNativeMethods, SevenZipNativeSession and ArchiveRuntimeResolver behind Core's callback contracts using T01's bridge. Load only trusted application-local matched engine/bridge assets; reject missing/mismatched runtimes without PATH lookup or fallback. Package native assets for existing supported publish targets, including reproducible build, notices, source availability and provenance. Replace the production SharpCompress dependency; retain old characterization through a test-only dependency where necessary, explicitly documenting its historical scope. Open selected-source parts read-only through confined callbacks; preserve indexed archive order, directories, links and duplicates. Inspect actual size, encrypted headers and bounded count fallback. Implement controlled output/skip callbacks, transient password input, session reopen and solid replay into discard streams. Translate definite failures separately from FR-19 indeterminate errors. No exceptions cross the ABI; pin delegates/handles until native calls return. Never expose PSP paths to the engine or use CLI extraction.
  - Done when: Production adapter tests cover T01's proven fixture matrix, ordered entry bytes/metadata and sizes, password retry/cancel, solid skip/replay, missing parts and late corruption. Incomplete/overflowing totals cannot silently use SizeBytes as authoritative. No full temporary extraction or whole-game memory buffering is used. Source parts remain byte-identical and all handles close. Build/full regression pass.
  - Revision 0.2 completion gate: native adapter integration also proves cancellation/error callback lifetime, correct Unicode/ABI marshalling, runtime resolution/version checks and publish asset/licence/source completeness. Exercise FR-19 reopen/retry and definite corruption separately on Linux/Windows; unavailable required native cases leave this task open. Historical managed rejections cannot satisfy native acceptance.
  - Evidence: not started.

- [ ] **T04. Provide confined Linux filesystem access.** FR-3, FR-4, FR-5, FR-6, FR-10, FR-12, FR-18, NFR-1, NFR-4
  - Depends on: T02.
  - Work: Implement the shared FileGameCopyFileSystem facade and LinuxCopyFileAccess: identity-bound source/destination root sessions, free bytes, handle-relative traversal, link rejection, nested directories and explicit output streams. Implement absent-target exclusive creation and evidence-checked overwrite without truncation before validation. Follow destination-native case semantics and report incompatible names/file-directory conflicts. Add deterministic fault injection for space/open/write/close errors without introducing deletion/restore APIs.
  - Done when: Linux integration tests with dedicated temporary roots verify expected bytes, no preflight mutation, existing-target consent tokens, target replacement before open/truncate, parent-link swaps, sibling-prefix traversal, duplicate/case behavior, volume-evidence mismatch and closed handles. Unsafe entries cannot produce outside writes; failures retain output. Native helper tests pass on Linux; simulated device identities are explicitly distinguished from real PSP evidence. Build/full regression pass.
  - Evidence: not started.

- [ ] **T05. Provide confined Windows filesystem access.** FR-3, FR-4, FR-5, FR-6, FR-10, FR-12, FR-18, NFR-1, NFR-4
  - Depends on: T02, T04.
  - Work: Add WindowsCopyFileAccess behind the same filesystem contract using volume/handle evidence, reparse-aware confinement and conditional mutation. Cover Windows drive roots, reserved/incompatible names, file/directory conflicts and destination-native case behavior. Reuse existing device identity observations without adding or changing mount/removal providers. Keep platform dispatch in Infraestructure.
  - Done when: Windows native integration tests verify the same mutation/containment guarantees as T04, including reparse/parent/target swaps, changed volume identity, exclusive creation, overwrite evidence and handle release. Cross-platform tests/architecture guards and repository build/regression pass. If Windows is unavailable, document exact unexecuted commands/cases and future Windows environment needs; the native completion gate and this task remain open, rather than marking fake-adapter tests as Windows proof.
  - Evidence: not started; Windows environment availability must be reassessed.

- [ ] **T06. Complete the single-operation copy engine.** FR-3, FR-4, FR-5, FR-6, FR-7, FR-8, FR-9, FR-10, FR-12, FR-13, FR-14, FR-15, FR-18, FR-19, NFR-2, NFR-4
  - Depends on: T03, T04, T05.
  - Work: Implement GameCopyService using injected contracts. Determine actual size/free bytes before destination writes; stream archive contents with one combined byte-progress calculation. Process entries in order, prompt per overwrite and re-prompt on target change; skip refused/unsafe/link entries, including all-skipped archives. Fail on incompatible names or structural conflicts. Cancel/fail closes streams and retains every destination file as written with a destination warning. Release transient password references and reject obsolete prompt/progress responses.
  - Done when: Deterministic fake-I/O tests and real archive/filesystem integration cover exact/insufficient/unknown space, SizeBytes mismatch, all conflict outcomes, password retry, multipart failure, empty/late corruption, cancel during input/read/write, close failure and terminal races. Compare retained bytes before/after cancel/failure and confirm no deletion/restoration. Success publishes only after file release; progress does not claim successful completion on failure. Source hashes/catalogue spies remain unchanged. Build/full regression pass.
  - Revision 0.2 scope and verification: Use ordered extraction-target callbacks. On FR-19 stop the native invocation and close current outputs before prompting, retain written files and the active slot, then offer explicit password retry/cancel. Revalidate source/device evidence and remaining capacity; replay completed solid entries to discard without duplicate writes, while the failed/current entry uses normal overwrite consent. Progress excludes replayed writes. Test this alongside definite corruption, target changes, cancellation and callback failures.
  - Evidence: not started.

- [ ] **T07. Coordinate active copies with device identity and safe removal.** FR-2, FR-3, FR-9, FR-10, FR-11, FR-16, FR-19, NFR-1, NFR-4
  - Depends on: T06.
  - Work: Implement GameCopySessionCoordinator with one active slot, captured Spec 004 associations and session observation. Reject stale/missing/unready source/PSP evidence, require the PSP's single usable volume and preserve no-fallback rules. Fail on relevant selections, roots, generations or availability changes; tolerate unrelated global revisions. Add optional awaited removal interception to DevicesViewModel and a coordinator reservation so copy start cannot race a pending removal. Stop matching copies and release their files before forwarding safe removal for the same freshly revalidated device; do not rewrite device detection rules.
  - Done when: Coordinator and device-command tests demonstrate no PSP/multiple PSP choice, one active copy, relevant versus unrelated changes, replacement devices at the same path, repeated/stale actions and no implicit redirection. A removal spy proves stop/stream release precedes removal, the same request continues, removal failure remains visible and unrelated devices retain existing behavior. Existing device boundary/session tests, build and full regression pass.
  - Revision 0.2 scope and verification: Keep the active slot while FR-19 input is pending. Test selection changes, device loss and removal during that prompt: invalidate it, stop/release native work and continue the same removal request with fresh device evidence. Late prompt responses cannot resume a retired operation.
  - Evidence: not started.

- [ ] **T08. Expose the typed copy screen and interactions.** FR-1, FR-2, FR-6, FR-7, FR-8, FR-9, FR-10, FR-11, FR-13, FR-16, FR-19, NFR-2, NFR-3, NFR-4
  - Depends on: T07.
  - Work: Add GameCopyViewModel, typed GameCopyView and MainViewModel.Copy; compose adapters/coordinator through CopyStartup with the existing caller-owned session. Add the explicit catalogue action, per-game/source/PSP selection integration, one row/table, combined progress, masked password retry/cancel, per-file Yes/Skip/Cancel and terminal notifications. Keep catalogue-only consumers working and preserve visibility of preparation/device errors without silent auto-retry. Use existing Avalonia controls, keyboard access, automation labels and bounded scrolling; initialization-only view code-behind.
  - Done when: Awaited headless tests cover compiled bindings, game/destination display, no selected game versus no PSP messages, responsive pending work, actual-size progress, per-file renewed prompts, password retry/cancel, some/all-skipped completion and explicit retained-output warnings. Selecting another game/source/PSP fails the copy as specified. Old prompt/progress responses cannot affect another operation. Minimum-size layout and any ViewLocator mapping are checked. Boundary, catalogue regression, build and full tests pass.
  - Revision 0.2 scope and verification: Display “Incorrect password or damaged archive” for FR-19, with masked password retry and cancel. Headless tests must distinguish that decision from definite failure, verify retained-output messaging and renewed overwrite consent for the partial current file, and keep one active row while input is pending.
  - Evidence: not started.

- [ ] **T09. Integrate confirmed shutdown and lifetime race handling.** FR-9, FR-10, FR-11, FR-16, FR-17, FR-19, NFR-2, NFR-3, NFR-4
  - Depends on: T08.
  - Work: Integrate copy into ApplicationDeviceLifetime/App's existing single shutdown flow. Confirm before cancelling/detaching devices; coalesce duplicate close requests. Decline preserves the copy/application. Accept cancels the current copy, resolves pending prompts, awaits file release and then disposes copy/device owners and exits without deleting PSP output. Cover late provider construction, startup failures, window/OS close, disposal, copy completion races and source/destination removal interleavings. Preserve device cleanup failure reporting and native authorization limitations.
  - Done when: Awaited MainWindow/lifetime functional tests demonstrate accept/decline/duplicate close, close while password/overwrite/I/O is pending, close racing completion or device startup, no late attachment/callback and handles released before exit. Prior completed-copy files remain as written; no output cleanup occurs. Existing provider lifetime/close tests and catalogue/device regressions pass. Repository build and full tests pass; any unavailable graphical smoke check is explicitly recorded for T10.
  - Revision 0.2 scope and verification: Include close/removal/completion races during FR-19 input or native callbacks. Await native return before disposing delegates, source/output handles or the runtime; stale callbacks and accepted shutdown cannot restart extraction. Add awaited lifetime evidence for these cases.
  - Evidence: not started.

- [ ] **T10. Consolidate regression, coverage and mandatory native copy evidence.** FR-1–FR-19, NFR-1–NFR-4; all acceptance and completion criteria
  - Depends on: T01–T09.
  - Work: Run final build/full regression and sequential coverage with the planned owning-component report; justify gaps without inventing a numeric target. Prepare and execute the plan's Linux/Windows graphical and real-PSP/source-media matrix, including multiple PSP choice, capacity, real filesystem semantics, password/multipart, overwrite/reconfirmation, retained output, removal and confirmed closure. Record procedures/results in manual-validation.md and coverage.md, cross-reference Spec 004's outstanding native dependencies, and maintain pending-validation.md for unavailable cases with future setup/restart steps. Compare source/catalogue state and expected output hashes. Do not claim that documentation repairs an unavailable test.
  - Done when: Build/regression and coverage evidence are current; each FR/NFR/AC maps to actual automated/manual evidence; mandatory physical/native cases pass on both platforms with controlled test media. If any required environment/hardware/check is unavailable or failing, keep T10 open with exact blockers/procedures and report incomplete validation. No ordinary startup runs against arbitrary media and no unapproved provisioning/destructive native operation occurs. Completion of T10 permits requesting authorization for SDD validation/code review; it does not itself mark the specification implemented or grant acceptance.
  - Revision 0.2 scope and verification: Include FR-19 retry/cancel and definite corruption in automated and physical matrices. Verify packaged native engine/bridge version, ABI, assets and licence/source provenance for supported publish targets; distinguish actual native/publish results from reference CLI and historical managed tests.
  - Evidence: not started; physical/native prerequisites and approved test media remain to be established.

## Requirement coverage index

| Requirement | Tasks |
| --- | --- |
| FR-1 | T08, T10 |
| FR-2 | T07, T08, T10 |
| FR-3 | T02–T07, T10 |
| FR-4 | T02–T06, T10 |
| FR-5 | T01, T03–T06, T10 |
| FR-6 | T04–T06, T08, T10 |
| FR-7 | T02, T06, T08, T10 |
| FR-8 | T02, T06, T08, T10 |
| FR-9 | T02, T06–T09, T10 |
| FR-10 | T02, T04–T09, T10 |
| FR-11 | T02, T07–T09, T10 |
| FR-12 | T02, T04–T06, T10 |
| FR-13 | T01, T03, T06, T08, T10 |
| FR-14 | T01, T03, T06, T10 |
| FR-15 | T01, T03, T06, T10 |
| FR-16 | T07–T10 |
| FR-17 | T09, T10 |
| FR-18 | T02, T04–T06, T10 |
| FR-19 | T01–T03, T06–T10 |
| NFR-1 | T01, T03–T05, T07, T10 |
| NFR-2 | T03, T06, T08–T10 |
| NFR-3 | T02, T08–T10 |
| NFR-4 | T01–T10 |

## Approval record and next step

- Specification 0.5 and plan 0.2 are explicitly approved. The latest “apruebo el plan” authorizes this revised decomposition, not implementation.
- Tasks 0.1 and its managed-backend T01 were explicitly authorized; their historical evidence and unaffected responsibilities are retained. The user approved tasks 0.2 and native T01 resumption, then explicitly deferred unavailable Windows checks and authorized T02.
- T01 remains open for Windows/native evidence; its passing Linux results do not establish Windows/physical acceptance. T02 is complete on feature/005_T02_transfer-contracts-rules with the documented dependency exception and passing regression.
- Next: report T02 and stop. T03 has not started and needs explicit authorization; the user has not waived any remaining Windows or physical completion criterion.
