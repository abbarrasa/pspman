using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class GameArchiveReaderEdgeTests
{
    private static SevenZipGameArchiveReader Reader() => new(GameArchiveReaderTests.RuntimeRoot);

    [ArchiveAdapterTheory]
    [InlineData("Rar.encrypted_filesOnly.rar", GameCopyErrorCode.IndeterminateEncryptedError)]
    [InlineData("Rar.encrypted_filesAndHeader.rar", GameCopyErrorCode.IndeterminateEncryptedError)]
    [InlineData("Rar5.encrypted_filesOnly.rar", GameCopyErrorCode.IncorrectPassword)]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", GameCopyErrorCode.IndeterminateEncryptedError)]
    [InlineData("Zip.deflate.WinzipAES.zip", GameCopyErrorCode.IncorrectPassword)]
    public async Task Wrong_password_is_typed_and_reopen_retry_uses_a_new_session(string name, GameCopyErrorCode expected)
    {
        await using var source = new TestArchiveSource(name);
        var target = new TestArchiveTarget();
        var error = await Assert.ThrowsAsync<GameCopyException>(async () =>
        {
            await using var failed = await Reader().OpenAsync(source, "incorrect", default);
            var info = await failed.InspectAsync(default);
            await failed.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, default);
        });
        Assert.Equal(expected, error.Error.Code);
        Assert.Equal(0, source.OpenStreams);
        Assert.Equal(target.Prepared.Count, target.Completed.Count);
        await using var retry = await Reader().OpenAsync(source, "test", default);
        var inspection = await retry.InspectAsync(default);
        var result = new TestArchiveTarget();
        await retry.ExtractAsync(inspection.Entries.Select(e => e.Index).ToImmutableArray(), result, null, default);
        Assert.Equal(3, result.Outputs.Count);
    }

    [ArchiveAdapterFact]
    public async Task Damaged_encrypted_rar_retains_output_and_returns_indeterminate_error()
    {
        using var area = new ProbeDirectory();
        var bytes = File.ReadAllBytes(Fixture("Rar.encrypted_filesOnly.rar")); bytes[50000] ^= 0x10;
        File.WriteAllBytes(Path.Combine(area.Path, "damaged.rar"), bytes);
        await using var source = new TestArchiveSource("damaged.rar", area.Path);
        await using var session = await Reader().OpenAsync(source, "test", default);
        var info = await session.InspectAsync(default);
        var target = new TestArchiveTarget();
        var error = await Assert.ThrowsAsync<GameCopyException>(() => session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, default).AsTask());
        Assert.Equal(GameCopyErrorCode.IndeterminateEncryptedError, error.Error.Code);
        Assert.Equal("Incorrect password or damaged archive", error.Message);
        Assert.Contains(target.Outputs.Values, b => b.Length != 0);
        Assert.Equal(target.Prepared.Count, target.Completed.Count);
    }

    [ArchiveAdapterTheory]
    [InlineData("Rar5.multi.part01.rar", "Rar5.multi.part02.rar")]
    [InlineData("Rar2.multi.rar", "Rar2.multi.r00")]
    [InlineData("WinZip26.nocomp.multi.zip", "WinZip26.nocomp.multi.z01")]
    public async Task Missing_parts_are_distinct_from_password_errors(string first, string missing)
    {
        using var area = new ProbeDirectory();
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(Fixture(first))!, first.Split('.')[0] + "*"))
            if (Path.GetFileName(path) != missing) File.Copy(path, Path.Combine(area.Path, Path.GetFileName(path)));
        await using var source = new TestArchiveSource(first, area.Path);
        var error = await Assert.ThrowsAsync<GameCopyException>(async () =>
        {
            await using var session = await Reader().OpenAsync(source, null, default);
            var info = await session.InspectAsync(default);
            await session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), new TestArchiveTarget(), null, default);
        });
        Assert.Equal(GameCopyErrorCode.MissingPart, error.Error.Code);
    }

    [ArchiveAdapterFact]
    public async Task Duplicate_unicode_paths_skips_and_empty_files_keep_distinct_indices()
    {
        using var area = new ProbeDirectory();
        using (var zip = ZipFile.Open(Path.Combine(area.Path, "duplicate.zip"), ZipArchiveMode.Create))
        {
            foreach (var value in new[] { "first", "second" })
            {
                using var writer = new StreamWriter(zip.CreateEntry("nested/🎮.iso").Open()); writer.Write(value);
            }
            zip.CreateEntry("empty.iso");
        }
        await using var source = new TestArchiveSource("duplicate.zip", area.Path);
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        Assert.Equal(3, info.Entries.Length);
        Assert.Equal(info.Entries[0].Path, info.Entries[1].Path);
        Assert.Equal("nested/🎮.iso", info.Entries[0].Path);
        var target = new TestArchiveTarget { Prepare = (_, _) => ValueTask.FromResult(new ArchiveEntryTarget(null, GameCopySkipReason.OverwriteDeclined)) };
        await session.ExtractAsync([0, 1, 2], target, null, default);
        Assert.Equal(new[] { 0, 1, 2 }, target.Prepared);
        Assert.Empty(target.Outputs);
        Assert.Equal(3, target.Completed.Count);
    }

    [ArchiveAdapterTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Crc_damage_stops_at_failed_entry_and_closes_retained_outputs(bool late)
    {
        using var area = new ProbeDirectory();
        var path = Path.Combine(area.Path, "bad.zip"); ArchiveCompatibilityEdgeTests.CreateStoredZip(path);
        var bytes = File.ReadAllBytes(path);
        var offset = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(late ? "second-unique-payload" : "first-unique-payload"));
        bytes[offset] ^= 0x10; File.WriteAllBytes(path, bytes);
        await using var source = new TestArchiveSource("bad.zip", area.Path);
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var target = new TestArchiveTarget();
        var error = await Assert.ThrowsAsync<GameCopyException>(() => session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, default).AsTask());
        Assert.Equal(GameCopyErrorCode.CorruptArchive, error.Error.Code);
        Assert.Equal(late ? 2 : 1, target.Outputs.Count);
        Assert.Equal(target.Prepared.Count, target.Completed.Count);
    }

    [ArchiveAdapterFact]
    public async Task Empty_zip_reports_empty_without_output_callbacks()
    {
        using var area = new ProbeDirectory();
        using (ZipFile.Open(Path.Combine(area.Path, "empty.zip"), ZipArchiveMode.Create)) { }
        await using var source = new TestArchiveSource("empty.zip", area.Path);
        await using var session = await Reader().OpenAsync(source, null, default);
        var error = await Assert.ThrowsAsync<GameCopyException>(() => session.InspectAsync(default).AsTask());
        Assert.Equal(GameCopyErrorCode.EmptyArchive, error.Error.Code);
    }

    [ArchiveAdapterFact]
    public async Task Source_identity_change_stops_before_output_preparation()
    {
        await using var source = new TestArchiveSource("Rar5.rar");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        source.Changed = true;
        var target = new TestArchiveTarget();
        var error = await Assert.ThrowsAsync<GameCopyException>(() => session.ExtractAsync([0], target, null, default).AsTask());
        Assert.Equal(GameCopyErrorCode.SourceChanged, error.Error.Code);
        Assert.Empty(target.Prepared);
    }

    [ArchiveAdapterTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_dispose_while_preparation_is_pending_waits_for_native_return(bool dispose)
    {
        await using var source = new TestArchiveSource("Rar5.solid.rar");
        var session = await Reader().OpenAsync(source, null, default);
        try
        {
            var info = await session.InspectAsync(default);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var target = new TestArchiveTarget { Prepare = async (_, ct) => { entered.TrySetResult(); await never.Task.WaitAsync(ct); return new(null, GameCopySkipReason.Link); } };
            using var cancellation = new CancellationTokenSource();
            var worker = session.ExtractAsync(info.Entries.Select(e => e.Index).ToImmutableArray(), target, null, cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            GC.Collect(); GC.WaitForPendingFinalizers();
            if (dispose) await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            else cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { await session.DisposeAsync(); }
        Assert.Equal(0, source.OpenStreams);
    }

    [ArchiveAdapterFact]
    public async Task Selected_solid_index_replays_prior_entries_without_output_callbacks()
    {
        await using var source = new TestArchiveSource("Rar5.solid.rar");
        await using var session = await Reader().OpenAsync(source, null, default);
        var info = await session.InspectAsync(default);
        var file = info.Entries.First(e => !e.IsDirectory && e.Index > 0);
        var target = new TestArchiveTarget();
        await session.ExtractAsync([file.Index], target, null, default);
        Assert.Equal(new[] { file.Index }, target.Prepared);
        Assert.Equal(file.SizeBytes, target.Outputs[file.Index].LongLength);
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExtractAsync([file.Index, file.Index], target, null, default).AsTask());
    }

    [Fact]
    public async Task Absent_runtime_never_falls_back_to_installed_tools()
    {
        using var area = new ProbeDirectory();
        await using var source = new TestArchiveSource("Zip.deflate.zip");
        var error = await Assert.ThrowsAsync<GameCopyException>(() => new SevenZipGameArchiveReader(area.Path).OpenAsync(source, null, default).AsTask());
        Assert.Equal(GameCopyErrorCode.UnsupportedArchive, error.Error.Code);
        Assert.Equal(0, source.OpenStreams);
    }

    [ArchiveAdapterTheory]
    [InlineData("Version")]
    [InlineData("Abi")]
    [InlineData("Rid")]
    [InlineData("SourceSha256")]
    [InlineData("engine-bytes")]
    [InlineData("missing-notice")]
    [InlineData("missing-source")]
    public async Task Mismatched_or_incomplete_packages_fail_before_archive_io(string change)
    {
        using var area = new ProbeDirectory();
        var relative = Path.Combine("runtimes", ArchiveRuntimeResolver.CurrentRuntimeIdentifier, "native");
        CopyTree(Path.Combine(GameArchiveReaderTests.RuntimeRoot, relative), Path.Combine(area.Path, relative));
        var native = Path.Combine(area.Path, relative);
        var manifest = Path.Combine(native, "runtime.json");
        if (change == "engine-bytes") File.AppendAllText(Path.Combine(native, OperatingSystem.IsWindows() ? "7z.dll" : "7z.so"), "wrong");
        else if (change == "missing-notice") File.Delete(Path.Combine(native, "LICENSE.7zip.txt"));
        else if (change == "missing-source") File.Delete(Path.Combine(native, "7z2606-src.tar.xz"));
        else
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!;
            if (change is "Version" or "Abi") document[change] = 999;
            else document[change] = "mismatch";
            File.WriteAllText(manifest, document.ToJsonString());
        }
        await using var source = new TestArchiveSource("Zip.deflate.zip");
        var error = await Assert.ThrowsAsync<GameCopyException>(() => new SevenZipGameArchiveReader(area.Path).OpenAsync(source, null, default).AsTask());
        Assert.Equal(GameCopyErrorCode.UnsupportedArchive, error.Error.Code);
        Assert.Equal(0, source.OpenStreams);
    }
    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var child in Directory.EnumerateDirectories(from)) CopyTree(child, Path.Combine(to, Path.GetFileName(child)));
    }
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers", name);
}
