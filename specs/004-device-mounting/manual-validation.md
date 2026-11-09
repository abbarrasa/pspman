# Device mounting — Recognition evidence and native validation

Specification: approved [spec.md](./spec.md), revision 0.3
Plan: approved [plan.md](./plan.md), revision 0.1
Evidence scope: T01 documentation research only; FR-2 and NFR-1.
Research date: 2026-11-09

## Verdict and evidence levels

T01's documentary matrix is complete. All-model PSP recognition and Linux/Windows device behavior are **not yet verified**. No PSP was connected, inspected, mounted or ejected for this task, and PSPMan's device feature is not implemented. This document does not certify any physical console, firmware or OS combination.

- **Manufacturer documented:** Sony documentation establishes a feature or operating prerequisite; it does not establish a USB VID/PID mapping unless explicitly stated.
- **Registry documented:** a public USB ID dataset names a device presentation; this is not manufacturer certification, a complete descriptor capture or proof of a model-family mapping.
- **Protocol documented:** the authors of a USB protocol/tool identify its own presentation; this does not establish ordinary mass-storage availability.
- **Pending:** no suitable descriptor evidence or physical validation is recorded. Pending is not a failed hardware test and does not establish impossibility.

## Manufacturer sources

| ID | Primary source | Supported observation and limitation |
| --- | --- | --- |
| S1 | [Sony PSP USB Connection guide](https://manuals.playstation.net/document/en/psp/current/settings/usb.html) | Describes connecting a powered PSP to a USB mass-storage-capable computer and selecting USB Connection. It does not enumerate model-specific VID/PID values or certify Linux. |
| S2 | [Sony USB transfer compatibility page](https://support.sony.jp/electronics/support/articles/00364538) | Explicitly lists PSP-1000/2000/3000 and PSP-N1000 for USB-connected transfers with the listed Sony products. This is evidence of USB capability, not PSPMan or OS validation. |
| S3 | [Sony PSP Go announcement/specifications](https://www.sony.com/en/SonyInfo/IR/news/8ido18000001tb03-att/PSPgo.pdf), printed page 4 | Identifies N1000, USB 2.0, 16 GB internal flash and a Memory Stick Micro slot. Does not prove simultaneous USB volume exposure, descriptor values or how storage selections enumerate. |
| S4 | [Sony PSP-E1002/E1003 instruction manual](https://www.playstation.com/content/dam/global_pdc/en/corporate/support/manuals/psp-docs/ENUK_PSP-E1002_E1003-6.50.pdf), printed pages 13, 27 and 31 | Describes USB mode, Memory Stick Duo use through a PC and troubleshooting recognition with USB mass-storage support. Does not supply USB IDs. |

The console must expose a storage interface before host mounting is possible. PSPMan must not claim that cable connection alone guarantees this or change firmware/USB mode itself. This is an operating prerequisite from S1/S4, consistent with the approved specification's exclusion of console mode changes.

## Model-family and storage-presentation matrix

| Family | Storage presentation to investigate | Source/evidence status | USB recognition gap | Linux physical check | Windows physical check |
| --- | --- | --- | --- | --- | --- |
| PSP-1000 | Console USB storage with inserted game-storage media | USB capability documented by S1/S2 | No model-bound full descriptor capture; generic registry entries below do not establish this family's exact ID/interface mapping. | Pending | Pending |
| PSP-2000 | Console USB storage with inserted game-storage media | USB capability documented by S1/S2; registry names a PSP Slim presentation | Do not equate the registry's Slim label with exhaustive 2000-series/firmware coverage without descriptors. | Pending | Pending |
| PSP-3000 | Console USB storage with inserted game-storage media | USB capability documented by S1/S2 | No model-bound VID/PID/interface evidence established in this research. | Pending | Pending |
| PSP Go / PSP-N1000 | Internal flash through console USB storage | Internal flash and USB documented by S1/S2/S3 | Exact storage-mode IDs/interfaces, root count and identity persistence remain pending. | Pending | Pending |
| PSP Go / PSP-N1000 | Inserted Memory Stick Micro through console USB storage, if exposed | Card slot documented by S3; host exposure must be observed separately | Do not assume internal/card storage share an ID, appear simultaneously or retain the same volume identity. | Pending | Pending |
| PSP Street / PSP-E1000 | Console USB storage with inserted Memory Stick Duo | Model-specific USB/storage instructions in S4 | No model-bound VID/PID/interface evidence established in this research; regional variants remain untested. | Pending | Pending |

Regional variants and firmware revisions are not certified by a family name. This matrix records the required family coverage without excluding any model from the approved requirement. An empty slot or a console not exposing storage is not evidence of a mountable destination.

## USB identifiers: bounded evidence, not an approved allowlist

The [hwdata USB ID dataset](https://github.com/vcrhonek/hwdata/blob/73fa04de426aefe192505fcbc6c24237a9fd9c9b/usb.ids) was read at immutable revision `73fa04de426aefe192505fcbc6c24237a9fd9c9b`. Its Sony vendor section contains these entries:

| VID:PID | Dataset name | Interpretation for later T15 |
| --- | --- | --- |
| `054c:01c8` | PSP Type A | Registry candidate only; require observed mass-storage interface and its association with an accessible storage device. No exact model-family assignment is established here. |
| `054c:01c9` | PSP Type B | Not sufficient evidence for a mountable destination: the PSPLINK project uses this pair for its development protocol, as documented below. |
| `054c:02d2` | PSP Slim | Registry candidate only; no inference that all slim models, regional revisions or firmware modes share this pair. |

These are public product identifiers, not unique console serials. Do not identify every Sony USB device as a PSP, infer a model from a display string, or infer unique device identity from VID/PID. No full positive mass-storage descriptor fixture was obtained during T01.

The protocol authors' [PSPLINK udev rule](https://github.com/pspdev/psplinkusb/blob/2e92bc9a88eff16ef3e5face341cb46d131fd56f/usbhostfs_pc/50-psplink.rules) explicitly uses `054c:01c9`; their [PSPLINK setup documentation](https://pspdev.github.io/psplink/linux.html) explains its development-tool context. This is primary evidence for that protocol, not Sony certification of ordinary mass storage. PSPMan must not create a mounted destination solely from this pair. No PSPLINK software, drivers or udev permission changes were installed.

## Feasibility assessment and T15 gate

- No consulted source demonstrates that USB-based recognition of a supported model is impossible. Missing descriptors are an evidence gap, not proof of impossibility.
- Current evidence is insufficient to implement or certify an exhaustive all-model recognition table. T15 must obtain provenance-backed storage descriptors or sanitized real-device fixtures for the presentations it implements; it must disclose unsupported evidence instead of silently reducing the all-model requirement.
- Each positive fixture needs model family, firmware/mode, USB device and interface descriptors, and the link to the OS storage/volume object. Unique serials must be omitted or replaced with explicit synthetic fixture values.
- A USB device class alone may not describe an interface on a composite device. Confirm the actual storage interface and OS storage association rather than infer mountability from a registry label.
- Negative cases must include Sony non-PSP storage, generic storage with PSP-looking labels/directories, unknown descriptors and a development-protocol presentation without mountable storage.
- Reconnection testing must separate console identity, inserted medium identity and volume identity. A recognized model is not proof that the same physical device or card returned.
- If later evidence demonstrates a model cannot be recognized from USB information, stop the affected recognition implementation and request a specification decision. Do not introduce a manual PSP declaration or a filesystem heuristic without approval.

## Native validation record — not executed in T01

| Required evidence | Current status | Owning tasks |
| --- | --- | --- |
| Linux USB recognition and mount behavior for each family/storage presentation above | Pending; documentary sources are not native observations | T49–T50, T53 |
| Windows USB recognition and mount behavior for each family/storage presentation above | Pending; no Windows run in T01 | T51–T53 |
| Startup/hot insertion, native authorization allow/deny/cancel, all-volume roots | Pending | T49, T51 |
| Source-group choice, multi-volume source root and per-game PSP destinations | Pending | T50, T52 |
| Whole-device safe removal, busy-volume rejection and optical-media ejection | Pending | T50, T52 |
| Same-session reconnection with changed roots; restart clears preferences and re-enables mounting | Pending | T50, T52 |
| Main catalog remains interactive while authorization/mount/removal is pending | Pending | T50, T52 |

Later native entries must record the actual OS, console family/firmware/storage presentation, procedure, expected/observed results and remaining gaps without unique device identifiers. No copying, archive probing, formatting or forced removal belongs in these procedures.

## T01 completion evidence

- Each requested family has a manufacturer reference, explicit descriptor-evidence status and separate Linux/Windows physical-validation status.
- Public USB registry values were checked against the pinned source; PSPLINK's product ID was cross-checked against its authors' pinned rule and documentation.
- No product code, automated tests, dependency files, system configuration or user devices were modified. Automated build/test execution is not applicable to this documentation-only task.
- Documentary readiness does not satisfy final all-model or native-platform acceptance. Those remain pending for their owning tasks.
