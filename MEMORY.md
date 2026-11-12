# Project memory

Durable project context; see [AGENTS.md](./AGENTS.md) for standing instructions.

## Implemented state

- PSPMan catalogs PSP games across removable media; CRUD and CSV export/import/template flows are implemented. Deployment, scanning and copying are not.
- Core, Infraestructure and Desktop have xUnit suites. Preserve the existing Infraestructure spelling and dependency direction.
- Desktop composes SQLite at the OS local application-data path PSPMan/catalog.db. Corrupt startup/read state is reported without repair and disables catalog mutations.
- Avalonia typed bindings cover catalog/details, editing, confirmed deletion, CSV export/import and template destinations. Exact byte counts plus binary-unit sizes appear in editor/details; CSV exports integer bytes only.

## Domain and persistence

- Identity is Medium plus ordinal case-sensitive Location, with normalized DVD Number only for DVD; edits preserve IDs. Location is medium-rooted slash syntax, not a host path, and is validated without file probes.
- DVD needs a positive Int32 number; zero normalizes to null. Non-DVD number is null; SizeBytes is positive Int64; Notes permit 2,500 UTF-16 code units.
- SQLite uses parameterized CRUD and separate case-sensitive DVD/non-DVD indexes. Existing catalogs are validated before transactional migration; malformed data fail without recovery. Later connections use ReadWrite to avoid recreating a missing catalog.
- Microsoft.Data.Sqlite 10.0.13 and SQLitePCLRaw.bundle_e_sqlite3 2.1.14 avoid the older bundle vulnerability warning. Save/import results retain confirmed writes when refresh subsequently fails; corruption is distinct from ordinary errors.

## CSV contracts

- Shared schema: Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes. Medium labels Dvd/Usb/MemoryCard, formats ISO/CHD; no IDs or approximate sizes.
- Semicolon CSV uses strict UTF-8, optional import BOM/export BOM, quoting, escaped quotes and multiline Notes. Export prefixes formula-like text with a tab; import removes exactly one tab if the next non-whitespace character is =, +, - or @.
- Import headings are case-insensitive and reordered/extra columns work; all seven are required, last duplicate wins. Whitespace in headings or Medium/Format labels is rejected.
- Short rows fail; surplus cells and blank/spaces-tabs-only physical lines outside quotes are ignored. Errors identify logical-record start lines; fatal quoting/decoding/read failures discard the preview.
- Only the last valid row per identity remains; invalid later duplicates cannot replace it. Identical records, including null/empty Notes equivalence, cause no proposal/count/write; empty Notes clears existing text.
- Preview reads one immutable catalog snapshot without writes. Confirmation consumes proposals once, creates IDs or preserves edit IDs, retains successful writes, continues ordinary save failures and stops on corruption/source errors.
- SHA-256 covers complete raw source bytes and held-handle/current-path evidence before writes and after EOF. Repeated hashing favors detection; sharing is not an absolute cross-platform lock.
- No fixed CSV size limit; tokenizer record buffering is bounded but candidate/error memory is input-dependent. An unchanged template yields three invalid rows; header-only input yields zero proposals/errors.
- Template saving reuses protected export/destination validation and explicit replacement consent without reading/changing the catalog; normal export resets template mode.

## Desktop pitfalls

- Preview/confirm work is off the UI thread and resumes on the caller UI context. Cancel discards late preview results; saving cannot be cancelled. CRUD is guarded during evaluation, review and apply.
- ViewLocator replaces ViewModel with View without altering namespaces. Preserve typed bindings and align explicit availability properties with commands.
- Headless tests use Avalonia.Headless 12.1.4, FluentTheme and a nonparallel collection with disposable catalogs/files; flush dispatcher jobs after selection before keyboard actions.
- For async headless callbacks use explicit Dispatch<bool>(async () => { await work; return true; }, token), as in MainWindowDeviceIntegrationTests. Bare await Dispatch(async () => ...) can await only outer Task<Task>; earlier DevicesViewTests/GameCatalogImportFlowTests async evidence needs an approved corrective audit. Capture old commands, not recycled button instances, when checking stale actions.
- Never run coverage instrumentation and another test run against shared binaries concurrently: it caused BadImageFormatException; rebuild and run sequentially. MainViewModel has mixed legacy line endings; preserve unrelated lines and check added-line whitespace.
- The auto-PR workflow targets dev and requires GitHub Actions PR permission. The feature skill requires approved plans; its validator needs PyYAML.

## Device mounting state and safety

- [Spec 004](./specs/004-device-mounting/spec.md) revision 0.3, [plan](./specs/004-device-mounting/plan.md) revision 0.1 and [tasks](./specs/004-device-mounting/tasks.md) revision 0.1 are approved. T01–T43 implementation is complete after individual approvals; native evidence and the older async headless audit are separately pending.
- Core owns session-only source/volume/per-game PSP associations, serialized operations/authorization, explicit retries, whole-device removal, success-only remount suppression and stale-work rejection. PSP recognition needs provenance-backed Sony descriptors plus storage evidence, never labels/directories or VID/PID as unique identity.
- Linux uses pinned Tmds.DBus.Protocol 0.94.1 and UDisks2, excludes internal disks, retains empty readers and separates reader/card identity. Empty native options, no force/shell/credentials; actual-state refresh and native errors remain authoritative. Private-bus tests cover transport decoding/cleanup, not hardware/Polkit.
- Windows inventory/notifications use SetupAPI, Configuration Manager, storage IOCTLs and GUID volumes. Storage-number pairs group current disks, not reconnection identity; shared hubs never merge devices. UNIQUEID-backed non-location USB identity requires session uniqueness; device identity is not inserted-media identity.
- Windows generations change for observed GUID/filesystem-serial/availability changes or disconnects, not labels/roots. Observer registration precedes inventory, callbacks only signal bounded/coalesced worker reconciliation, and disposal releases registrations/temporary handles.
- Windows mount reuses roots or assigns free D–Z letters after revalidation, protecting logical/DOS mappings and partition policy. Whole-device removal uses ordinary lock/flush, fresh topology and exact-node PnP removal, never shared hubs/force. CONFIGRET 5 is not Win32 AccessDenied 5; native veto details survive partial failure.
- Optical ejection retains the physical reader and requires locks plus independent fresh inventory/native empty-media confirmation. Missing volumes alone do not prove an empty tray; never reuse an old storage-number pair for an empty reader.
- Elevated operations use strict bounded/versioned UTF-8 JSON framing, closed fields and one fixed authenticated operation. Random local restricted pipes reject remote clients and validate bidirectional native PID/creation/image/user/session/elevation evidence; request IDs correlate, not authenticate. Generic dotnet.exe fails closed.
- ShellExecuteEx/runas is only for classified privilege failure on a dedicated STA worker. Cancel cannot dismiss OS UAC: hold the global prompt slot until native return and dispose late handles. Fixed helper dispatch runs before Avalonia/catalog; malformed helpers exit 2 without UI fallback. Replies never replace independent native confirmation; prompt suppression resets only on explicit retry or confirmed lifecycle changes.
- ProviderFactory/DeviceStartup are independent of catalog creation, report service errors and own resources without starting discovery. App activation remains T44. Per-device bridge revisions ignore unrelated changes but reject changed media; Linux waits/disposes pending signal reads correctly.
- DevicesViewModel projects immutable observations on an injected UI context, rejects stale/late callbacks, withholds unusable roots and keeps good siblings. PSP rows are distinct from source rows. It neither starts nor owns the session.
- Source/volume changes use Core rules, independent optical/shared-removable preferences, explicit multi-volume choice and no silent fallback. Source paths retain complete Location and explicit host syntax with no file probes. Removal/retry availability matches commands; Core exposes failed-mount evidence and revalidates queued optional observations. Native acceptance is not completion.
- MainViewModel has optional caller-owned Devices injection and selected-game metadata forwarding with host-default/injected syntax. Typed destination choices carry game ID/input version/revision/media evidence; immediate invalidation, UI guards and the shared association gate prevent stale retargeting/lost preferences. Reliable reconnection uses current roots; unavailable explicit choices never fall back. No catalog writes or App activation.
- DevicesView uses compiled typed templates, source/PSP/other groups, nested volumes, English state/native errors, source/volume action selectors and retry/removal actions. Observable options delegate existing validated commands; identifiers/roots distinguish missing/duplicate labels. T43 embeds it in MainWindow with per-game PSP options, explicit availability and 900 × 450 layout/keyboard tests. A view-only converter keeps catalog-only widths unchanged and removes the unused device column; code-behind only initializes. App composition/discovery remains T44, not started; authorize the separate older-headless audit before relying on its historical evidence.

## Verification and pending acceptance

- Latest T43: eight fully awaited integration cases passed after a verified red run. Build: 0 warnings/errors. Regression reported 1391 passed (Core 619, Infraestructure 495, Desktop 277), 4 Windows-native skips, 1395 total; focused coverage run reported 90 passed. These counts do not certify older async callbacks affected by the pending audit. Ordinary Linux/X11 startup/window with isolated catalogs passed; injected device UI is verified headlessly until real composition in T44. No real device operations.
- Focused coverage: destination action options/presentation and MainViewModel device integration 100% lines/branches; destination synchronous code 100%/87.50%, async body 95%/75%. Width converter 75%/100%, excluding its deliberately unsupported ConvertBack. Defensive exception/cancellation remains incomplete; compiled XAML and fully awaited new UI cases provide binding/layout evidence. See [tasks](./specs/004-device-mounting/tasks.md) for commands, results and evidence limitations.
- [Pending native validation](./specs/004-device-mounting/pending-validation.md) documents Windows QEMU/KVM VM, optional Docker-managed VM/CI, read-only/security/UAC/apphost/hardware and Linux Polkit steps with safety/evidence gates. No provisioning/workflow changes or native execution is implied; exhaustive PSP models/hardware remain pending.
- Historical [CRUD](./specs/001-crud-game/coverage.md), [export](./specs/002-export-games/coverage.md) and [import](./specs/003-import-games/coverage.md) evidence remains authoritative. Import measured 639 passing tests; owning-suite line/branch coverage: Core 98.09%/96.13%, Infraestructure 97.08%/85.19%, Desktop 89.65%/87.69%.
- [Import graphical validation](./specs/003-import-games/manual-validation.md) observed Linux pickers/template/preview/apply/error flows, including stopped writes with partial retained saves after removing a temporary CSV. Decoded-stream failure is automated; physical device removal, Windows/macOS native dialogs, Spanish-regional Excel display/formula nonexecution and Excel-edited round trip remain unverified.
