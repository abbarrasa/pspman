# Archive backend evaluation — plan revision 0.2

## Scope and authorization

After the initial [T01 report](./archive-compatibility.md), the user authorized reviewing the plan/evaluating another engine. This evaluation uses official downloads in an isolated temporary directory and existing public test fixtures. No system-wide installation, native bridge, production dependency replacement, device mounting, PSP writes or T02 implementation occurred. Proposed changes are in [draft plan 0.2](./plan.md).

## Alternatives

| Option | Evidence | Decision |
| --- | --- | --- |
| SharpCompress 0.50.9 | Existing T01 probes fail encrypted multipart RAR and encrypted LZMA ZIP; older RAR password errors overlap format errors. | Do not assume it fulfills all approved archive behavior. Keep historical evidence. |
| Built-in ZIP | No RAR engine; cannot replace the full required backend. | Insufficient for this feature. |
| libarchive | The inspected official RAR5 source's HEAD_CRYPT branch returns a fatal encryption-not-supported error. | Does not resolve required encrypted RAR behavior; no implementation trial justified by this evidence. |
| Official 7-Zip 26.06 | Linux reference executable validates 16 fixture sets and decodes both previously failing sets to the expected original hashes. Full source supplies indexed archive extraction and source/output/password callbacks. | Proposed full native engine plus a small C ABI bridge, subject to actual bridge/platform gates and an encrypted-error decision. |
| Production CLI wrapper | Reference CLI is useful to verify fixtures, but filename-text parsing, duplicate selection, password arguments and direct filesystem extraction do not match the controlled-stream architecture. | Reference oracle only; do not launch it with real user passwords as the product backend. |

Primary references: [official downloads](https://www.7-zip.org/download.html), [7-Zip developer FAQ](https://www.7-zip.org/faq.html), [7-Zip distribution licence](https://www.7-zip.org/license.txt), [release archive interfaces](https://github.com/ip7z/7zip/blob/26.06/CPP/7zip/Archive/IArchive.h), [release client example](https://github.com/ip7z/7zip/blob/26.06/CPP/7zip/UI/Client7z/Client7z.cpp), [libarchive RAR5 handler](https://github.com/libarchive/libarchive/blob/master/libarchive/archive_read_support_format_rar5.c). The libarchive finding describes the inspected source, not an exhaustive claim about every release/fork.

## Reproducible release inputs

Official release: **7-Zip 26.06 (2026-11-06)**. Downloads followed the official website links to ip7z/7zip release assets; no third-party package or reduced p7zip executable was used.

| Download | SHA-256 computed locally |
| --- | --- |
| [7z2606-linux-x64.tar.xz](https://github.com/ip7z/7zip/releases/download/26.06/7z2606-linux-x64.tar.xz) | `2b539cf067b2ad48e1baa5093dbaa8818b73542d1222a0ffde9dc5f2922ea62e` |
| [7z2606-src.tar.xz](https://github.com/ip7z/7zip/releases/download/26.06/7z2606-src.tar.xz) | `a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4` |

These hashes identify the retrieved evidence; they are not claimed as independent publisher signatures. Release source, build inputs and notices need proper native-runtime packaging in authorized implementation.

## Executed reference checks

Environment: Linux x86-64. Downloaded official `7zz` reports 26.06. Only public fixture passwords were supplied to these diagnostic commands. Use an isolated evaluation directory; do not run the extraction examples against a PSP or user output directory.

Representative command from repository root, where `7zz` denotes the downloaded official reference executable:

```sh
7zz t -ptest -bd tests/PSPMan.Infraestructure.Tests/Fixtures/Transfers/Rar.EncryptedParts.part01.rar
7zz t -ptest -bd tests/PSPMan.Infraestructure.Tests/Fixtures/Transfers/Zip.lzma.WinzipAES.zip
```

Both exited **0** with successful integrity tests. The RAR report identified six volumes, three files and 100926 decompressed bytes. The ZIP report also identified three files/100926 bytes; it displayed Minor_Extra_ERROR characteristics but completed its test successfully. Do not suppress future native API warnings simply because this reference test returned 0.

Sixteen sets were tested with the correct public fixture password (or a supplied password ignored by an unencrypted archive); every command exited 0:

- ZIP deflate, ZIP64, ZIP WinZip AES, traditional-encryption ZIP (password `12345678`) and split ZIP (`WinZip26.nocomp.multi.zip` plus `.z01`).
- RAR4, RAR5 and solid RAR5.
- RAR4 and RAR5 encrypted data and encrypted headers (four sets).
- Modern RAR5 multipart and legacy RAR multipart (two sets).
- Encrypted multipart RAR and encrypted LZMA ZIP (two previously failing sets).

For the last two sets, `7zz x -ptest -y -bd -o<isolated-output> <fixture>` was run separately into two newly created temporary directories after listing/inspecting the known archive paths. No archived executable was executed. Every output matched the independently documented originals:

| Relative path | Size | SHA-256, identical for both extractions |
| --- | ---: | --- |
| `exe/test.exe` | 45056 | `8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106` |
| `jpg/test.jpg` | 40372 | `b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53` |
| `тест.txt` | 15498 | `4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9` |

This independently establishes that the previously unverified encrypted multipart sample is readable by a reference engine and contains the expected original payload; the initial T01 uncertainty about fixture validity is resolved. It does not prove the as-yet-unimplemented adapter.

## Inherent encrypted-error ambiguity

Two independent checks were performed with the original single-volume `Rar.encrypted_filesOnly.rar`:

1. Run its test with password `incorrect`: exit 2, data error in an encrypted file, with a wrong-password question.
2. Create a private copy whose byte at zero-based offset 1000 is XORed with 1, leaving the shared fixture unchanged. Run that damaged copy with the **correct** password `test`: exit 2, the same data-error/wrong-password-question diagnostic for the affected file.

The extracted release's RAR handler confirms the reason: CRC mismatch yields kCRCError and decoder S_FALSE yields kDataError, including encrypted items. Those results do not by themselves determine whether a key was wrong or ciphertext damaged. The archive API has kWrongPassword for cases the format can identify, but that cannot be assumed for every encrypted variant. A new backend alone cannot supply a universal distinction.

Proposed product clarification for that undecidable case: stop I/O, retain output, display **“Incorrect password or damaged archive”**, and allow explicit password retry or cancellation. This proposal is not an approved requirement. If declined, the user must choose another observable outcome for ambiguous encrypted failures; neither decoder has evidence permitting a definite causal claim in those cases.

## Proposed integration and outstanding verification

The full native archive API supports entry-index extraction with caller-provided output streams. The proposed bridge translates that API to a versioned C ABI; managed Infraestructure supplies verified source-part handles and controlled destination streams. This allows existing Core path checks, per-file confirmations, duplicate entries, skips and byte accounting to remain authoritative. Passwords would use callbacks, not process arguments. The bridge/engine would be bundled by runtime architecture with explicit version checks and the release's distribution/source notices.

The official release licence and developer FAQ identify LGPL and additional code-specific conditions. Carry the actual licence texts and required source/build information with redistributed assets and review the bridge packaging accordingly; this document does not replace those licence terms. The proposed full engine is distinct from the reduced, more permissively licensed LZMA SDK, which is not a complete RAR backend.

Not executed here: native bridge construction, managed callback/ABI integration, interruption/cancellation under that API, per-index duplicate/skip behavior, conditional PSP output, runtime publish packaging, native Windows execution, physical PSP validation or a new native advisory audit. A CLI test does not pass these gates. The visible environment did not expose a C++ compiler; toolchain provisioning/build verification belongs to a subsequently approved task. No replacement package or native asset was added to the repository.

## Scope and approval impact

Specification revision 0.4 is unchanged and remains the approved baseline. Draft plan 0.2 proposes replacing the failed managed engine. Tasks 0.1 have not been rewritten: affected native-engine/contracts/adapter/integration/evidence work needs a new approved task revision after the behavioral decision and plan approval. T01 remains open and T02 has not started. Earlier .NET build/regression/audit results remain scoped to the managed prototype; no rerun was necessary for this documentation-only plan revision.
