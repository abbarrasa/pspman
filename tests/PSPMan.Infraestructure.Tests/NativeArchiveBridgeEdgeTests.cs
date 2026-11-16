using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PSPMan.Infraestructure.Tests;

public sealed class NativeArchiveBridgeEdgeTests
{
    [NativeBridgeFact]
    public void Abi_and_pinned_source_version_are_explicit_and_incompatible_abi_is_rejected()
    {
        Assert.Equal(1u, NativeArchiveProbe.Abi);
        Assert.Equal(2606u, NativeArchiveProbe.EngineVersion);
        var error = Assert.Throws<InvalidOperationException>(() => new NativeArchiveProbe("Zip.deflate.zip", abi: 99));
        Assert.Contains("8", error.Message);
    }

    [NativeBridgeTheory]
    [InlineData("Rar.encrypted_filesOnly.rar", 4)]
    [InlineData("Rar.encrypted_filesAndHeader.rar", 4)]
    [InlineData("Rar5.encrypted_filesOnly.rar", 3)]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", 4)]
    [InlineData("Zip.deflate.WinzipAES.zip", 3)]
    public void Password_errors_stop_and_reopening_with_correct_password_succeeds(string name, int expected)
    {
        using (var failed = new NativeArchiveProbe(name, "incorrect"))
        {
            var result = failed.Open();
            if (result == 0) result = failed.Extract();
            Assert.Equal(expected, result);
            // Leading directory entries may finish before the first encrypted file.
            if (failed.Ended.Count != 0) Assert.Equal(expected, failed.Ended.Last().Result);
        }
        using var retry = new NativeArchiveProbe(name, "test");
        Assert.Equal(0, retry.Open());
        Assert.Equal(0, retry.Extract());
        Assert.Equal(3, retry.Outputs.Count);
    }

    [NativeBridgeFact]
    public void Damaged_encrypted_rar_and_wrong_password_have_same_indeterminate_result()
    {
        using var area = new ProbeDirectory();
        var source = Fixture("Rar.encrypted_filesOnly.rar");
        var bytes = File.ReadAllBytes(source);
        bytes[1000] ^= 0x10;
        File.WriteAllBytes(Path.Combine(area.Path, "damaged.rar"), bytes);
        using var probe = new NativeArchiveProbe("damaged.rar", "test", area.Path);
        Assert.Equal(0, probe.Open());
        Assert.Equal(4, probe.Extract());
        Assert.NotEmpty(probe.Outputs); // Retained decoded bytes, not a successful game.
        Assert.Equal(4, probe.Ended.Last().Result);
    }

    [NativeBridgeTheory]
    [InlineData("Rar.encrypted_filesAndHeader.rar")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar")]
    [InlineData("Zip.deflate.WinzipAES.zip")]
    public void Missing_password_is_input_required_without_automatic_writes(string name)
    {
        using var probe = new NativeArchiveProbe(name);
        var result = probe.Open();
        if (result == 0) result = probe.Extract();
        Assert.Equal(10, result);
        Assert.All(probe.Outputs.Values, bytes => Assert.Empty(bytes));
    }

    [NativeBridgeTheory]
    [InlineData("Zip.deflate.zip")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("Rar5.multi.part01.rar")]
    [InlineData("Rar.EncryptedParts.part01.rar")]
    public void Cancellation_from_progress_stops_native_call_and_closes_owned_files(string name)
    {
        var probe = new NativeArchiveProbe(name, "test");
        try
        {
            Assert.Equal(0, probe.Open());
            probe.OnProgress = p => { if (p.Decoded > 0) p.Cancel(); };
            Assert.Equal(1, probe.Extract());
            Assert.NotEmpty(probe.Outputs);
            Assert.True(probe.Written > 0);
            Assert.Equal(1, probe.Ended.Last().Result);
        }
        finally { probe.Dispose(); }
        Assert.Equal(0, probe.OpenInputCount);
        Assert.True(probe.ClosedInputs > 0);
    }

    [NativeBridgeFact]
    public void Callback_io_error_preserves_partial_output_and_original_managed_cause()
    {
        using var probe = new NativeArchiveProbe("Rar.encrypted_filesOnly.rar", "test") { FailWrite = true };
        Assert.Equal(0, probe.Open());
        Assert.Equal(2, probe.Extract());
        Assert.IsType<IOException>(probe.CallbackError);
        Assert.Contains(probe.Outputs.Values, bytes => bytes.Length > 0);
        Assert.Equal(2, probe.Ended.Last().Result);
    }

    [NativeBridgeFact]
    public void Source_callback_error_is_not_reclassified_as_password_failure()
    {
        using var probe = new NativeArchiveProbe("Rar.encrypted_filesAndHeader.rar", "incorrect") { FailRead = true };
        Assert.Equal(2, probe.Open());
        Assert.IsType<IOException>(probe.CallbackError);
        Assert.Empty(probe.Outputs);
    }

    [NativeBridgeTheory]
    [InlineData("Rar5.multi.part01.rar", "Rar5.multi.part02.rar")]
    [InlineData("Rar2.multi.rar", "Rar2.multi.r00")]
    [InlineData("WinZip26.nocomp.multi.zip", "WinZip26.nocomp.multi.z01")]
    public void Missing_parts_fail_without_source_changes(string first, string missing)
    {
        using var area = new ProbeDirectory();
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(Fixture(first))!, first.Split('.')[0] + "*"))
            if (Path.GetFileName(path) != missing) File.Copy(path, Path.Combine(area.Path, Path.GetFileName(path)));
        using var probe = new NativeArchiveProbe(first, directory: area.Path);
        var result = probe.Open();
        if (result == 0) result = probe.Extract();
        Assert.Equal(6, result);
    }

    [NativeBridgeFact]
    public void Empty_zip_metadata_is_empty_and_extract_writes_nothing()
    {
        using var area = new ProbeDirectory();
        using (ZipFile.Open(Path.Combine(area.Path, "empty.zip"), ZipArchiveMode.Create)) { }
        using var probe = new NativeArchiveProbe("empty.zip", directory: area.Path);
        Assert.Equal(0, probe.Open());
        Assert.Empty(probe.Metadata());
        Assert.Equal(0, probe.Extract());
        Assert.Empty(probe.Outputs);
    }

    [NativeBridgeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Definite_crc_corruption_stops_at_failed_entry_and_retains_output(bool late)
    {
        using var area = new ProbeDirectory();
        var path = Path.Combine(area.Path, "corrupt.zip");
        ArchiveCompatibilityEdgeTests.CreateStoredZip(path);
        var bytes = File.ReadAllBytes(path);
        var marker = Encoding.UTF8.GetBytes(late ? "second-unique-payload" : "first-unique-payload");
        bytes[bytes.AsSpan().IndexOf(marker)] ^= 0x10;
        File.WriteAllBytes(path, bytes);
        using var probe = new NativeArchiveProbe("corrupt.zip", directory: area.Path);
        Assert.Equal(0, probe.Open());
        Assert.Equal(5, probe.Extract());
        Assert.Equal(late ? 2 : 1, probe.Outputs.Count);
        Assert.Equal(5, probe.Ended.Last().Result);
    }

    [NativeBridgeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Indexed_duplicate_unicode_paths_and_skips_preserve_order(bool skipAll)
    {
        using var area = new ProbeDirectory();
        var path = Path.Combine(area.Path, "duplicate.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var text in new[] { "first", "second" })
            {
                var entry = archive.CreateEntry("nested/日本語-🎮.iso", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        }
        var before = SHA256.HashData(File.ReadAllBytes(path));
        using var probe = new NativeArchiveProbe("duplicate.zip", directory: area.Path) { Skip = i => skipAll || i == 0 };
        Assert.Equal(0, probe.Open());
        var metadata = probe.Metadata();
        Assert.Equal(2, metadata.Length);
        Assert.All(metadata, entry => Assert.Equal("nested/日本語-🎮.iso", entry.Path));
        Assert.Equal(new uint[] { 0, 1 }, metadata.Select(e => e.Entry.Index));
        Assert.Equal(0, probe.Extract());
        Assert.Equal(new uint[] { 0, 1 }, probe.Begun);
        Assert.Equal(skipAll ? 0 : 1, probe.Outputs.Count);
        Assert.Equal(11UL, probe.Decoded);
        Assert.Equal(skipAll ? 0UL : 6UL, probe.Written);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [NativeBridgeFact]
    public void Solid_skip_and_reopen_selected_index_do_not_duplicate_completed_outputs()
    {
        using var first = new NativeArchiveProbe("Rar5.solid.rar") { Skip = i => i != 2 };
        Assert.Equal(0, first.Open());
        var metadata = first.Metadata();
        Assert.Equal(100926UL, metadata.Where(e => (e.Entry.Flags & 1) == 0).Aggregate(0UL, (n, e) => n + e.Entry.Size));
        Assert.Equal(0, first.Extract());
        Assert.Single(first.Outputs);
        using var retry = new NativeArchiveProbe("Rar5.solid.rar");
        Assert.Equal(0, retry.Open());
        Assert.Equal(0, retry.Extract([2]));
        Assert.Equal(first.Outputs[2], retry.Outputs[2]);
        Assert.Equal(new uint[] { 2 }, retry.Begun);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers", name);

    [NativeBridgeTheory]
    [InlineData("Rar5.multi", null)]
    [InlineData("Rar.EncryptedParts", "test")]
    public void Multipart_callback_requests_follow_the_archive_part_sequence(string stem, string? password)
    {
        using var probe = new NativeArchiveProbe(stem + ".part01.rar", password);
        Assert.Equal(0, probe.Open());
        Assert.Equal(0, probe.Extract());
        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"{stem}.part{i:00}.rar"), probe.OpenedParts.Distinct());
    }

    [NativeBridgeFact]
    public async Task External_cancel_while_read_callback_is_pending_releases_handles_after_native_return()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var probe = new NativeArchiveProbe("Zip.deflate.zip");
        Assert.Equal(0, probe.Open());
        probe.OnRead = _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Read was not released.");
        };
        var worker = Task.Run(() => probe.Extract());
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            GC.Collect(); GC.WaitForPendingFinalizers();
            probe.Cancel();
            Assert.True(probe.OpenInputCount > 0); // Cannot free callback/handles before native return.
        }
        finally { release.Set(); }
        Assert.Equal(1, await worker.WaitAsync(TimeSpan.FromSeconds(10)));
        probe.Dispose();
        Assert.Equal(0, probe.OpenInputCount);
        Assert.Null(probe.CallbackError);
    }

    [NativeBridgeFact]
    public void Password_callback_cancellation_is_not_an_indeterminate_error()
    {
        using var probe = new NativeArchiveProbe("Rar5.encrypted_filesAndHeader.rar", "test");
        probe.OnPassword = p => p.Cancel();
        Assert.Equal(1, probe.Open());
        Assert.Empty(probe.Outputs);
    }

    [NativeBridgeFact]
    public void Link_metadata_is_exposed_without_the_native_engine_following_it()
    {
        using var area = new ProbeDirectory();
        using (var zip = ZipFile.Open(Path.Combine(area.Path, "link.zip"), ZipArchiveMode.Create))
        {
            var link = zip.CreateEntry("link");
            link.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
            using var writer = new StreamWriter(link.Open());
            writer.Write("../../outside");
        }
        using var probe = new NativeArchiveProbe("link.zip", directory: area.Path) { Skip = _ => true };
        Assert.Equal(0, probe.Open());
        Assert.NotEqual(0u, Assert.Single(probe.Metadata()).Entry.Flags & 2);
        Assert.Equal(0, probe.Extract());
        Assert.Empty(probe.Outputs);
    }
}
