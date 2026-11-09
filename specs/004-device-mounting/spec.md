# Spec 004 — Device detection and mounting

Status: approved
Revision: 0.3

## Context and objective

Collection owners need PSPMan to make their connected PSP consoles and game-storage media accessible before a future game-copy operation. Automatic detection and mounting, visible device states and current filesystem roots remove the need to discover drive letters or mount locations manually. Resolving source collisions and choosing a PSP per game prepares an unambiguous source/destination association while leaving game copying outside this iteration.

## Users and actors

- A collection owner connecting PSP consoles, CD/DVD media, USB storage or memory cards.
- The operating system, which reports devices, controls mounts and safe removal, and presents any required authorization dialogue.

## User stories

- US-1: As a collection owner, I want connected devices mounted automatically so I can use them without entering mount commands or drive letters.
- US-2: As a collection owner, I want to distinguish source media from PSP destinations so I can prepare the intended game transfer.
- US-3: As a collection owner, I want to choose the source when several mounted media can serve as an origin so that PSPMan uses my intended copy.
- US-4: As a collection owner, I want to select a PSP for each game when several are connected so that games can have different destinations.
- US-5: As a collection owner, I want safe removal and useful errors so I know when a device can be disconnected or an operation needs attention.

## Definitions

- **Source medium:** supported CD/DVD media, USB storage or a memory card containing catalogued game files. A PSP recognized as a destination is not also treated as an ordinary source USB device.
- **Filesystem root:** the current operating-system mount point or Windows drive root through which a mounted volume is accessible.
- **Game location:** the existing catalog Location, such as `/GAMES/Game.rar`, interpreted relative to the selected volume root. It contains neither a volume label nor a host mount point or drive letter. The leading slash denotes the medium root, not the host filesystem root.
- **Resolved game path:** the selected volume's current filesystem root combined with the complete stored Game Location while retaining that root. `/GAMES/Game.rar` on a volume mounted at `/media/collection` resolves to `/media/collection/GAMES/Game.rar`; if mounted as `E:\`, it resolves to `E:\GAMES\Game.rar`. No location component is removed or interpreted as a volume label.
- **Source group:** optical media (CD/DVD), or non-optical removable storage (USB and Memory Card together). Device classification is informative; USB versus Memory Card does not restrict source eligibility within the shared group. Catalog duplicate identity remains unchanged.
- **Source collision:** more than one mounted source device belongs to the applicable source group. File existence and DVD Number do not discriminate candidates.
- **Session:** one execution of PSPMan. Source preferences and per-game PSP choices are not restored after restarting the application.

## Functional requirements

- FR-1: WHEN PSPMan starts, THE SYSTEM SHALL detect already connected supported source media and USB-connected PSP consoles. WHEN supported media or a PSP are subsequently connected or inserted, THE SYSTEM SHALL detect them without requiring an application restart.
- FR-2: WHEN USB information identifies an accessible storage device as a PSP, THE SYSTEM SHALL display it as a PSP destination. THE SYSTEM SHALL support all PSP models exposing storage through USB and SHALL NOT infer PSP identity solely from a volume label or directory names. IF USB recognition is inconclusive, THEN THE SYSTEM SHALL report the recognition failure rather than silently assign a PSP destination.
- FR-3: WHEN supported source media or recognized PSP storage are detected and not mounted, THE SYSTEM SHALL request their automatic mounting. WHEN they are already mounted, THE SYSTEM SHALL use their existing filesystem roots without requesting a duplicate mount. THE SYSTEM SHALL mount all supported connected sources and recognized PSPs, subject to operating-system authorization and errors.
- FR-4: IF mounting or safe removal requires additional authorization, THEN THE SYSTEM SHALL request it through the operating system's authorization dialogue. IF authorization is denied or cancelled, THEN THE SYSTEM SHALL report that the operation did not complete, retain the observed device state and allow an explicit retry without repeatedly reopening the prompt merely because the device remains connected.
- FR-5: WHILE devices are being detected, mounted, removed or unavailable, THE SYSTEM SHALL show their current state and distinguish source media from PSP destinations. Each listed device SHALL expose its available identifying information, medium type, current filesystem root when mounted, and any operation error; unavailable information SHALL NOT be fabricated.
- FR-6: WHEN a device is mounted or its operating-system root changes, THE SYSTEM SHALL update the root used for subsequent path resolution. THE SYSTEM SHALL NOT replace the catalog's stored Location with a host-specific absolute path or change catalog identities or DVD numbers.
- FR-7: WHEN a catalogued game's source is evaluated, THE SYSTEM SHALL determine mounted candidate devices from its source group without checking game-file existence or using DVD Number. THE SYSTEM SHALL combine the selected volume's current root with the complete stored Location, retaining the root and all Location components without interpreting any component as a volume label.
- FR-8: WHEN no source preference applies and exactly one compatible source device is mounted, THE SYSTEM SHALL select it automatically. IF no compatible source is mounted, THEN THE SYSTEM SHALL report that the source is unavailable. THE SYSTEM SHALL NOT describe a constructed path as proof that the game file exists.
- FR-9: WHEN no source preference applies and several compatible source devices are mounted, THE SYSTEM SHALL ask the user to choose the active source without checking whether they contain the game files. THE SYSTEM SHALL NOT silently choose between these devices.
- FR-10: WHEN the user chooses an active source, THE SYSTEM SHALL apply it to all games in the corresponding source group. THE SYSTEM SHALL retain one independent preference for CD/DVD and one shared preference for USB/Memory Card; a later choice SHALL replace only the preference in its own group.
- FR-11: IF the selected source or volume is unavailable or inaccessible, THEN THE SYSTEM SHALL report the failure and SHALL NOT automatically select another source. THE SYSTEM SHALL allow an explicit change of source. Game-file existence and access checks SHALL be deferred to the future copy operation, whose failure must be reported rather than trigger automatic fallback.
- FR-12: WHEN several recognized, mounted PSP destinations are available, THE SYSTEM SHALL allow the user to choose a PSP separately for each game, and SHALL NOT impose one global destination choice on every game. WHEN only one recognized, mounted PSP is available, THE SYSTEM SHALL expose it as the available destination without presenting a multiple-PSP collision. IF none is available, THEN THE SYSTEM SHALL show that the destination is unavailable.
- FR-13: THE SYSTEM SHALL keep source preferences, volume associations and per-game PSP choices only for the current session. WHEN the same device is reliably recognized after reconnection in that session, THE SYSTEM SHALL recover its associations using its current volume roots. IF identity cannot be established reliably, THEN THE SYSTEM SHALL NOT recover associations solely from an old drive letter, mount path or label. Restarting PSPMan SHALL require new choices where ambiguity exists.
- FR-14: WHEN a mounted PSP or source is disconnected or becomes inaccessible, THE SYSTEM SHALL update its availability and stop presenting its paths as usable. Affected game associations SHALL be shown as unavailable rather than silently redirected to another source or PSP. THE SYSTEM SHALL distinguish a failed or interrupted operation from successful mounting or removal.
- FR-15: WHEN the user requests safe unmount or eject for a PSP or source, THE SYSTEM SHALL request safe removal of the whole physical device, including its volumes, and display success only when confirmed by the operating system. IF any volume is busy or removal is rejected, THEN THE SYSTEM SHALL show the operating-system error and actual remaining mount states, permit retry, and SHALL NOT force removal or terminate other applications.
- FR-16: WHEN a device has been successfully unmounted or ejected through PSPMan, THE SYSTEM SHALL suppress automatic remount for that device during the current execution until disconnection/reconnection or media removal/reinsertion. WHEN PSPMan restarts, THE SYSTEM SHALL again automatically mount connected supported devices, reusing existing mounts.
- FR-17: THE SYSTEM SHALL present source availability, collision choice and the selected game's PSP destination as distinct user-visible concepts, with the relevant device names and roots visible before accepting a choice. Mount/removal state and errors SHALL be understandable without entering device identifiers, mount commands or filesystem roots manually.

- FR-18: WHEN a supported device exposes multiple volumes, THE SYSTEM SHALL request mounting of all available volumes and display their individual labels, roots and states under that device. WHEN the active source device has several mounted volumes, THE SYSTEM SHALL ask the user which volume to use as its source root and retain that selection only during the session. WHEN it has exactly one mounted volume, THE SYSTEM SHALL use that volume. Missing or duplicate labels SHALL NOT prevent selection: the current roots and available identifying information SHALL distinguish the displayed volumes.
- FR-19: WHEN a removable source cannot be distinguished as USB storage or a memory card from operating-system information, THE SYSTEM SHALL display it as Memory Card for information only; this classification SHALL NOT alter its shared USB/Memory Card source eligibility.

## Non-functional requirements

- NFR-1: This iteration SHALL support Linux and Windows. Platform-specific behavior SHALL preserve the same detection, association, failure and safe-removal rules, including native authorization dialogues where required. macOS support is deferred.
- NFR-2: Adding a future device Medium, such as network storage, SHALL preserve existing supported-medium behavior without changing existing game-association rules solely to accommodate the addition. Adding macOS SHALL preserve the existing rules and Linux/Windows behavior. The implementation plan SHALL explain both extension points and how their independence will be verified; no future provider is implemented in this iteration.
- NFR-3: PSPMan SHALL remain interactive during discovery, mounting, authorization waits and removal. Device state SHALL be visible while an operation is pending; unrelated catalog viewing SHALL not wait for its completion. No numeric detection or mounting deadline is committed before platform feasibility is assessed.
- NFR-4: Product text and functional documentation SHALL be in English. Native authorization dialogues may follow the operating-system locale. Mount roots and ordinary filesystem paths SHALL follow the host platform's syntax without altering stored catalog values.
- NFR-5: The feature SHALL NOT format, repartition, repair media, copy games, create game files or change existing game contents. PSPMan SHALL NOT collect or store the credentials entered into an operating-system authorization dialogue. A mounted volume's availability SHALL not be described as proof that a future copy will succeed.

## Edge cases

- Devices present at startup and devices inserted later follow the same automatic discovery/mount policy (FR-1, FR-3). Empty optical drives/readers provide no usable game source until media are present.
- Existing operating-system mounts are reused. Duplicate notifications must not result in duplicate mounts or duplicate candidate devices for the same mounted source (FR-3, FR-7).
- Permission denial/cancellation, unavailable authorization services, unsupported filesystems and ordinary mount failures produce device-specific errors and no false ready state (FR-4, FR-5, FR-14).
- Multiple mounted devices in one source group require selection regardless of file contents or DVD numbers. USB and Memory Card share eligibility and preference; CD/DVD remains independent (FR-7–FR-10, FR-19).
- This iteration does not probe game files. Future copy-time file errors must not cause automatic source fallback (FR-11).
- PSP destinations are separate from game sources. Destination choices can differ between two games in the same session (FR-2, FR-12).
- Device disappearance or changing mount roots must not leave a usable stale path or automatically redirect an existing association to a different device (FR-6, FR-13, FR-14).
- A successful explicit unmount must not trigger an automatic-remount loop; a rejected safe-removal request must not be presented as success (FR-15, FR-16).
- Multiple mounted source volumes require explicit session-only selection regardless of their labels. Missing/duplicate labels do not change stored Location or prevent volume selection. Whole-device removal reports failures from any volume (FR-15, FR-18).
- Reconnecting a recognized device restores session choices with refreshed roots; restarting clears choices but re-enables automatic mounting (FR-1, FR-13, FR-16).
- Unrecognized PSP USB information produces a recognition error rather than an assumed destination (FR-2).

## Out of scope

- Copying, deploying or decompressing games; creating destination directories; choosing a destination subdirectory on the PSP; modifying the PSP's USB mode or firmware.
- Game-content scanning/indexing, automatic catalog creation, content hashing and validation of game archives. Game-file existence/access checks are deferred to the future copy feature; this iteration resolves device and volume roots only.
- Manually declaring an arbitrary USB device to be a PSP, or identifying a PSP only from its filesystem contents.
- Formatting, partitioning, forced unmount, media repair and killing processes that hold a device busy.
- macOS, network-source implementation and persistent source/destination preferences.
- Changing CRUD/import duplicate identity: DVD Number remains part of catalog identity for DVD records, although it is not usable for mounted-source collision detection.

## Acceptance criteria

- AC-1 (FR-1–FR-6, NFR-1): On Linux and Windows, demonstrate discovery at startup and hot insertion, recognition from PSP USB information, reuse of existing mounts, automatic mounting and visible native authorization approval/denial, without rewriting catalog Location.
- AC-2 (FR-7–FR-11, FR-19): Given multiple mounted USB/card devices, choose one shared active source without file probes; independently choose an optical source. Changing either preference preserves the other. DVD Number does not discriminate candidates; an unavailable selected source reports an error without fallback.
- AC-3 (FR-12–FR-14): Given multiple recognized PSPs, choose different destinations for two games, disconnect a selected device and observe unavailable associations. Reconnect the same reliably identified device at a different root and verify recovery of session associations with the new root. Restart and verify that choices are not restored but connected devices mount again.
- AC-4 (FR-15, FR-16): Successfully remove a source and a PSP, verify absence of automatic remount during that execution until reinsertion, and demonstrate whole-device busy-volume rejection without force or false success.
- AC-5 (FR-17, NFR-3, NFR-4): Operate the source/destination controls with empty, pending, mounted, collision and error states; identify each selected root from English product feedback while catalog viewing remains responsive.
- AC-6 (NFR-2): Review the eventual plan's platform and Medium extension boundaries and verify that extending one does not require replacing the existing association behavior. macOS/network implementations remain excluded.
- AC-8 (FR-6, FR-7, FR-18): Mount a multi-volume source device and explicitly choose its source volume, including cases with missing or duplicate labels. Combine its root with an existing `/GAMES/Game.rar` Location without removing any directory or rewriting the catalog. With a single mounted source volume, verify automatic use of its root.
- AC-7 (NFR-5): Verify that these workflows do not invoke game copy/decompression, destructive media operations or credential collection. Any filesystem maintenance performed by the operating system during mounting is not a PSPMan game-content operation.

## Completion criteria

- Resolve blocking questions and approve this specification before planning implementation.
- Trace every FR/NFR to appropriate unit, integration and functional tests, run required build/regression checks, and measure line/branch coverage with significant gaps explained.
- Record native-platform evidence on Linux and Windows for detection, PSP recognition, authorization, roots, safe removal and disconnection. Identify unavailable hardware/platform checks as pending; test doubles alone do not certify real-device support.
- Demonstrate the main source/destination workflow with source collisions, per-game PSP choices, device/volume failure without fallback and no game-file probes, and session-only selections.
- Complete the constitution-compliance review and explicitly separate observed results from assumptions or unavailable verification.

## Open questions

- OQ-1–OQ-3 — Resolved through user clarification: all USB-storage-capable PSP models; whole-device mounting/removal with per-volume roots; independent optical and shared USB/Memory Card source preferences. USB identity feasibility and hardware coverage must be assessed in the plan, not assumed proven by this specification.
- OQ-4 — Resolved: Location remains a path within a volume and never includes its label. For a source device with several mounted volumes, the user selects the volume used as the active source root for the session. Existing records remain unchanged; no catalog migration is required.
- No outstanding clarification questions. Revision 0.3 was explicitly approved by the user before planning.

## Interaction references

The user's references include media creation and device-management applications. [balenaEtcher](https://etcher.balena.io/) presents a staged selection workflow, and [Rufus](https://rufus.ie/en/) is a device-oriented USB utility. They inform the separation of source/destination selection and visible device state; PSPMan's exact layout belongs in the plan. Their imaging, flashing, formatting and copying operations are not requirements for this feature.
