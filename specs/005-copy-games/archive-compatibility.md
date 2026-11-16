# T01 — Pinned archive compatibility gate

Specification: approved revision 0.4. Plan/tasks: approved revision 0.1.
Branch: `feature/005_T01_archive-compatibility`.

## Verdict

Historical initial gate report. Subsequent user-authorized [backend evaluation](./archive-backend-evaluation.md) validates the encrypted multipart fixture independently with 7-Zip and proposes a new backend; it does not retroactively pass the SharpCompress gate.

**Gate not passed; T01 remains open.** The pinned backend decodes the tested plain, encrypted single-volume and unencrypted multipart families, but the encrypted multipart sample fails through both asynchronous and synchronous sequential readers. Wrong-password errors are not uniformly distinguishable from corruption for older RAR encryption. Encrypted LZMA ZIP is also unsupported. These observations require a backend/requirement decision under the approved plan before dependent work; no approved requirement has been narrowed.

Passing characterization tests include tests that assert these known failures. Their success must never be reported as complete ZIP/RAR support or as acceptance of FR-13/FR-14.

## Changes made

- Added pinned SharpCompress 0.50.9 to Infraestructure only; no transfer production adapter or UI command was added.
- Added test-local probes, 33 upstream fixtures, MIT licence, fixture SHA-256 manifest and independently derived original payload hashes. Fixture provenance and CRLF/CP866 details are in the [fixture README](../../tests/PSPMan.Infraestructure.Tests/Fixtures/Transfers/README.md).
- Registered tasks approval and T01 authorization. No T02 work, device mounting, copy to a PSP, commits or pushes occurred. Pre-existing local deletions were preserved.

## Executed matrix

All rows below were exercised with the pinned package on Linux, .NET SDK 10.0.404 / test runtime 10.0.15. Native Windows and PSP hardware behavior is not inferred.

| Family or condition | Fixture / method | Observed result |
| --- | --- | --- |
| Plain ZIP | `Zip.deflate.zip` | All three decoded byte hashes, sizes and paths match with known CP866 corpus encoding. |
| ZIP64 | `Zip.zip64.zip` | All three decoded hashes and total 100926 bytes match. |
| Encrypted ZIP AES | `Zip.deflate.WinzipAES.zip`, password `test` | All expected payload hashes match. Incorrect password returns InvalidFormatException with message `bad password`; reopening with the correct password works. |
| Traditional-encryption ZIP | `Zip.deflate.pkware.zip`, password `12345678` | Wrong password returns CryptographicException; correct-password reopen succeeds. This sample has a different payload, so it is not included in the three-original-file hash claim. |
| Split ZIP | `WinZip26.nocomp.multi.zip` + `.z01` | Seekable ZipArchive reads all expected hashes. Starting at `.z01` is normalized by test-local code to the `.zip` control file and succeeds. Missing `.z01` produces a library/I/O failure. |
| RAR4 / RAR5 | `Rar4.rar`, `Rar5.rar` | Expected decoded hashes, sizes and paths match. |
| Solid RAR5 | `Rar5.solid.rar`, sequential RarReader | All expected decoded hashes match; cancellation during payload reading closes streams and allows reopen. |
| Encrypted single-volume RAR4 | `Rar.encrypted_filesOnly.rar`, `Rar.encrypted_filesAndHeader.rar` | Correct password decodes matching hashes. Wrong password produces ArchiveOperationException (size mismatch) or InvalidFormatException (unknown header), not a reliable dedicated password type. Correct-password reopen succeeds. |
| Encrypted single-volume RAR5 | `Rar5.encrypted_filesOnly.rar`, `Rar5.encrypted_filesAndHeader.rar` | Correct password decodes matching hashes; wrong password produces CryptographicException; retry succeeds. |
| Modern multipart RAR | `Rar5.multi.part01.rar`…`part06.rar` | Sequential reader with explicitly ordered streams decodes matching hashes. Starting from `part03` finds the same set. Missing part02 produces a library/I/O failure. Cancellation releases all caller-owned part streams and allows retry. |
| Legacy multipart RAR | `Rar2.multi.rar`, `.r00`…`.r05` | Numeric part order decodes matching hashes, including when requested from `.r02`; missing `.r00` fails. |
| Encrypted multipart RAR | `Rar.EncryptedParts.part01.rar`…`part06.rar` | All source parts are present and manifest-checked. Async and sync sequential readers both throw InvalidFormatException, `Unexpected end of encrypted stream`. Earlier random-access extraction also rejected multivolume decryption. This family is not proven compatible. |
| Encrypted LZMA ZIP | `Zip.lzma.WinzipAES.zip` | NotSupportedException, `LZMA with pkware encryption.` No support is inferred from the filename or extension. |
| Actual-size inspection | Plain/encrypted ZIP, split ZIP, RAR4, solid/encrypted/multipart RAR5 | Seven metadata cases report 100926 decompressed bytes before payload output. This is representative metadata evidence, not a proof for every codec/variant. |
| Empty archive | Generated empty ZIP | Zero entries/payload. |
| Early/late corruption | Generated stored ZIP with changed first/second payload byte | Explicit CheckCrc extraction rejects the corrupted payload as InvalidFormatException, not a password-specific error. |
| Cancellation | ZIP, solid RAR5 and multipart RAR5 | Deterministically cancelled during payload processing. OperationCanceledException, explicit file-handle release and successful reopen verified on private fixture copies. |
| Source integrity | 33-fixture SHA-256 manifest | Original fixture bytes match downloaded upstream values. Tests never execute archived binaries or write to shared fixture files. |

The encrypted multipart sample is a known upstream rejection case, not an independently validated good archive from a reference extractor. Its decoded bytes could not be verified in this environment. T01 therefore leaves the required encrypted-multipart support unproven rather than concluding that every encrypted multipart RAR is unsupported. A follow-up decision should include an independent known-good/reference-validated encrypted multipart sample when evaluating another backend or workaround.

## API findings relevant to a revised implementation plan

- Use the seekable ZIP API for real `.zip`/`.z01` sets. RAR readers need the complete explicitly ordered stream set; the single-file reader does not automatically provide the same multipart behavior.
- Archive `ExtractAllEntriesAsync` is reserved for solid archives; it is not a universal reader for ordinary RAR. Explicit sequential RarReader handles plain/solid/multipart variants with the ownership strategy tested here.
- The probe owns every opened part stream and disposes them independently of reader disposal. Relying only on the convenience file-enumerable reader left a part handle open after cancellation; caller ownership resolved the tested leak. Production must retain this ownership discipline.
- Raw entry streams alone do not guarantee CRC checking. Use explicit checksum validation when streaming into controlled output. ZIP CheckCrc behavior was tested; valid solid RAR was checked against independent SHA-256 payloads, not treated as proof of every solid corruption-detection path.
- Legacy filename encoding is not self-describing in the plain ZIP fixture. Explicit CP866 configuration recovers its original path; the default decoded replacement characters. This is evidence that production encoding assumptions need review, not authorization to use CP866 universally.
- RAR4 wrong-password failures overlap corruption/format failures. Do not classify every InvalidFormatException or size mismatch as a password problem, and do not silently remove password retry from the approved scope. ZIP AES has a distinct observed message but shares an exception type with corruption.
- No arbitrary archive size cap, full-game buffer, temporary full extraction, destination cleanup or original-file restoration was introduced. Hashing uses bounded buffers/discard sinks.

## Dependency and licence review

SharpCompress is pinned at 0.50.9. The restored net10.0 asset is `lib/net10.0/SharpCompress.dll` with no additional package dependencies for that target. NuGet package SHA-512 from restored assets:

```text
Jm32AbptP318P+G4Bew0c72X7R1MXqImenXD9kfkygysZJCTfXfp01CLKuFhGvJGIxSw/dutEAaAlwNOZ8MBlw==
```

The upstream MIT licence is retained with the fixtures. `dotnet list PSPMan.slnx package --vulnerable --include-transitive --format json` exited 0 and reported no known vulnerabilities for the solution at execution time. This is a point-in-time advisory check, not a security certification or universal archive safety guarantee.

## Verification record

- RED: 14 initial fixture/hash tests compiled and failed with the explicit unimplemented probe, demonstrating missing behavior rather than missing fixtures/test infrastructure.
- Intermediate API/fixture checks exposed incorrect generic-reader usage, a test-original LF/CRLF mismatch and a corruption fixture that initially modified a filename instead of payload. Those test/probe issues were corrected; they are not reported as library defects. The final source hashes are independently documented.
- Focused final characterization: 42 tests passed. Known-unsupported cases assert their actual rejection; the gate still fails its support criteria.
- `dotnet build PSPMan.slnx`: passed, zero warnings/errors.
- `dotnet test PSPMan.slnx`: passed, 1475 passed / 4 Windows-only skips / 0 failures (Core 618, Infraestructure 545 plus 4 skips, Desktop 312). The existing deleted placeholder Core test was preserved; the historical 619 Core count is not substituted for this run's actual 618.
- `git diff --check`: passed. Editor diagnostics for the changed test files and infrastructure project reported no errors; full compilation verifies the final test code.
- No graphical startup was run: application/UI behavior was unchanged, and ordinary startup would perform device discovery/mounting. No native PSP copy or physical acceptance check was executed. Coverage collection belongs to T10, not this compatibility gate.

## Next authorized boundary

Do not begin T02 or mark T01 complete. Present the reproduced archive/password limitations and seek authorization to revise the backend plan or clarify affected compatibility/error behavior. Preserve specification 0.4, plan/tasks approvals and completed prior features; no replacement dependency or requirement revision is authorized by this report. Physical/native validation remains mandatory and unexecuted for this feature.
