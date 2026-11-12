# Project memory

Durable context; see [AGENTS.md](./AGENTS.md) for standing instructions.

## Implemented state

- PSPMan catalogs removable-media PSP games; CRUD and CSV export/import/template flows are implemented. Deployment, scanning and copying are not.
- Desktop composes SQLite at the OS local application-data path PSPMan/catalog.db. Corrupt startup/read state is reported without repair and disables catalog mutations.
- Avalonia typed bindings cover catalog/details, editing, confirmed deletion, CSV flows and device selection. ViewLocator preserves namespaces when mapping ViewModel to View.
- Spec 004 revision 0.3, plan/tasks revision 0.1 are approved. T01–T46 and authorized AUD-1 corrections are complete; [tasks](./specs/004-device-mounting/tasks.md) hold approval/evidence. T47 is next, not started.

## Domain and persistence

- Identity is Medium plus ordinal case-sensitive Location, with normalized DVD Number only for DVD; edits preserve IDs. Location is medium-rooted slash syntax, not a host path, and is validated without file probes.
- DVD needs a positive Int32 number; zero normalizes to null. Non-DVD number is null; SizeBytes is positive Int64; Notes permit 2,500 UTF-16 code units.
- SQLite uses parameterized CRUD and separate case-sensitive DVD/non-DVD indexes. Existing catalogs are validated before transactional migration; malformed data fail without recovery. Later connections use ReadWrite to avoid recreating a missing catalog.
- Microsoft.Data.Sqlite 10.0.13 and SQLitePCLRaw.bundle_e_sqlite3 2.1.14 avoid the older bundle vulnerability warning. Save/import retain confirmed writes if refresh fails; corruption remains distinct.

## CSV contracts

- Schema: Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes; labels Dvd/Usb/MemoryCard and ISO/CHD. Exports use integer bytes, never IDs or approximate sizes.
- Semicolon CSV uses strict UTF-8, optional import BOM/export BOM, quoting, escaped quotes and multiline Notes. Export protects formula-like text with a tab; import removes exactly one tab only before the first non-whitespace =, +, - or @.
- Headings are case-insensitive and reordered/extra columns work; all seven are required, last duplicate wins. Heading whitespace and whitespace in Medium/Format labels are rejected.
- Short rows fail; surplus cells and blank/spaces-tabs-only physical lines outside quotes are ignored. Errors identify logical-record start lines; fatal quoting/decoding/read failures discard the preview.
- Only the last valid row per identity remains; invalid later duplicates cannot replace it. Identical records (including null/empty Notes equivalence) cause no proposal/write; empty Notes clears text.
- Preview uses one immutable catalog snapshot without writes. Confirmation consumes proposals once, preserves edited IDs, retains successful writes, continues ordinary save failures and stops on corruption/source errors.
- SHA-256 covers complete source bytes and held-handle/current-path evidence before writes and after EOF. Repeated hashing favors detection; sharing is not an absolute cross-platform lock.
- No fixed CSV size limit; record buffering is bounded but candidate/error memory is input-dependent. Unedited template yields three invalid rows; header-only input yields no proposals/errors.
- Template saving reuses protected export/destination validation and explicit replacement consent without catalog access; normal export resets template mode.

## Desktop and verification pitfalls

- Preview/apply run off UI and resume on its context; cancel discards late preview results, apply cannot be cancelled, and CRUD is guarded during evaluation/review/apply.
- Headless tests use Avalonia.Headless 12.1.4, FluentTheme and a nonparallel collection. Result-less async callbacks require HeadlessTestDispatcher.DispatchAsync or explicit Dispatch<bool>; bare Dispatch(async () => ...) can return Task<Task>. Import uses explicit Dispatch<int>.
- The [headless audit](./specs/004-device-mounting/headless-audit.md) corrected five result-less sites, a recycled-button assertion and a later Core/UI publication race. Await visible UI projection, not only Core's revision; capture retired commands, not recycled button instances, for stale-action checks.
- Never instrument coverage while another test/app uses shared binaries: it caused BadImageFormatException. Rebuild and run sequentially. Preserve mixed legacy line endings; check added-line whitespace.
- The auto-PR workflow targets dev and requires GitHub Actions PR permission. Feature planning requires approval; its validator needs PyYAML.

## Device state, lifetime and safety

- Core owns session-only source/volume/per-game PSP associations, serialized operations/authorization, whole-device removal, explicit retries, success-only remount suppression and stale-work rejection. Recognition needs provenance-backed Sony USB descriptors plus storage evidence, never labels/directories or VID/PID as unique identity.
- Linux uses pinned Tmds.DBus.Protocol 0.94.1 and UDisks2; excludes internal disks, retains empty readers and separates reader/card identity. Empty native options, no force/shell/credentials; actual-state refresh/native errors remain authoritative. Private-bus transport tests are not hardware/Polkit evidence.
- Windows uses SetupAPI/Configuration Manager, storage IOCTLs and GUID volumes. Storage-number pairs group current disks, not reconnection identity; shared hubs never merge devices. UNIQUEID-backed non-location USB identity needs session uniqueness; device identity is not media identity.
- Windows generations change for GUID/filesystem-serial/availability changes or disconnects, not labels/roots. Register observers before inventory; callbacks signal bounded/coalesced worker reconciliation; disposal releases registrations/handles.
- Windows mounting reuses roots or assigns free D–Z letters after revalidation, protects logical/DOS mappings and partition policy. Removal uses ordinary lock/flush, fresh topology and exact-node PnP scope, never shared hubs/force. CONFIGRET 5 is not Win32 AccessDenied 5.
- Optical ejection retains the reader and requires locks plus independent fresh empty-media confirmation. Missing volumes alone do not prove an empty tray; never reuse an old storage-number pair for an empty reader.
- Elevated operations use bounded/versioned strict UTF-8 JSON, closed fields and one fixed authenticated operation. Random restricted local pipes reject remote clients and validate bidirectional native PID/creation/image/user/session/elevation evidence; generic dotnet.exe fails closed.
- ShellExecuteEx/runas runs only after classified privilege failure on a dedicated STA worker. Cancellation cannot dismiss UAC: retain the prompt slot until native return and release late handles. Fixed helper dispatch precedes Avalonia/catalog; malformed helpers exit 2. Replies never replace native confirmation.
- ApplicationDeviceLifetime starts provider composition off UI on MainWindow.Opened, attaches initializing/ready/error Devices state without replacing MainViewModel, forwards the latest selected game, and keeps catalog/device failures independent. CatalogStartup and Core/native rules were not changed by T44.
- Normal close/ShutdownRequested cancels and detaches device UI immediately, then awaits construction/discovery/operation and provider/subscription cleanup before controlled exit. Duplicate requests share cleanup; late providers/callbacks cannot attach; cleanup failure exits 1. Forced process termination is not graceful shutdown evidence.
- DevicesViewModel observes caller-owned sessions, marshals immutable projections, rejects stale/late work and withholds unusable roots. Source/volume/PSP actions revalidate game/revision/media evidence through one association gate; no fallback, catalog writes or game-file probes.
- Compiled DevicesView groups sources/PSPs/other devices and shows nested roots, errors and retry/removal actions; MainWindow has per-game PSP choices and a 900 × 450 bounded workspace. Catalog-only consumers keep their layout. Initialization-only view code-behind contains no native/business logic.

## Latest evidence and pending acceptance

- T45/T46 now provide 16 fully awaited MainWindow/ApplicationDeviceLifetime flow cases. Controlled denial/cancellation/busy operations preserve keyboard browsing, visible errors and explicit retries; reliable reconnect recovers new keys/roots without fallback. Successful removal suppresses remount until reconnect/restart; restart reuses catalog IDs and exact removed-device keys but clears choices. Only tests/evidence changed in feature/004_T46_device-failure-reconnection-flow; no product repair, game files or native operations.
- Latest build: zero warnings/errors. Regression passed 1420 (Core 619, Infraestructure 495, Desktop 306), four Windows-native skips, 1424 total; sequential focused coverage passed 16/16. DeviceVolumeViewModel measured 100% lines/95.45% branches and DeviceAssociationRules 91.41%/80.47%. See [task evidence](./specs/004-device-mounting/tasks.md) for class/state-machine scope and gaps, not whole-feature totals; T48 owns that measurement.
- T44 Linux/X11 startup and normal Alt+F4 close passed with temporary catalog and a private D-Bus lacking UDisks activation; exit 0. Portal/FUSE warnings belonged to that isolated desktop environment. T45/T46 did not repeat native startup because production/UI code was unchanged.
- Ordinary startup now enables discovery/automatic mounting: do not launch against arbitrary hardware for smoke tests. [Pending native validation](./specs/004-device-mounting/pending-validation.md) retains Windows VM/CI/UAC/apphost/hardware and Linux Polkit procedures; native/all-model acceptance remains unverified.
- Historical [CRUD](./specs/001-crud-game/coverage.md), [export](./specs/002-export-games/coverage.md) and [import](./specs/003-import-games/coverage.md) evidence remains scoped to its revision. [Import graphical validation](./specs/003-import-games/manual-validation.md) observed Linux flows; physical removal, native Windows/macOS pickers and Excel formula/display/round-trip checks remain pending.
