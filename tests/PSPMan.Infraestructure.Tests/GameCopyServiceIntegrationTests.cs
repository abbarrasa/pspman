using System.IO.Compression;
using System.Security.Cryptography;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class GameCopyServiceIntegrationTests
{
    [LinuxArchiveFileSystemTheory]
    [InlineData("Zip.deflate.zip")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("Rar.EncryptedParts.part02.rar")]
    [InlineData("WinZip26.nocomp.multi.z01")]
    [InlineData("Rar.encrypted_filesAndHeader.rar")]
    public async Task Linux_engine_copies_real_archives_and_releases_all_handles(string name)
    {
        using var source = new CopyTestTree();
        using var destination = new CopyTestTree();
        await VerifyArchive(source.Root, destination.Root, source.Evidence, destination.Evidence,
            new RoutedFileSystem(source.FileSystem, destination.FileSystem), name);
        Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(source.Root));
        Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(destination.Root));
    }

    [WindowsCopyTheory]
    [InlineData("Zip.deflate.zip")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("Rar.EncryptedParts.part02.rar")]
    [InlineData("WinZip26.nocomp.multi.z01")]
    public async Task Windows_engine_copies_real_archives_and_releases_all_handles(string name)
    {
        using var source = new WindowsCopyTestArea();
        using var destination = new WindowsCopyTestArea();
        await VerifyArchive(source.Root, destination.Root, source.Evidence, destination.Evidence,
            new RoutedFileSystem(source.FileSystem, destination.FileSystem), name);
        Directory.Move(source.Root, source.Root + "-released");
        Directory.Move(destination.Root, destination.Root + "-released");
    }

    private static async Task VerifyArchive(string sourceRoot, string destinationRoot, CopyVolumeEvidence source,
        CopyVolumeEvidence destination, IGameCopyFileSystem fs, string name, string firstPassword = "test")
    {
        Stage(sourceRoot);
        var originals = Hashes(sourceRoot);
        var interaction = new Interaction { FirstPassword = firstPassword };
        var snapshots = new Snapshots();
        var result = await new GameCopyService(fs, new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot), interaction)
            .CopyAsync(Request(source, destination, name), snapshots);
        Assert.True(result.State == GameCopyState.Completed, result.Error?.Message);
        Assert.Equal(1m, snapshots.Values[^1].Progress!.Fraction);
        Assert.Equal(100926, snapshots.Values[^1].Progress!.Work.TotalBytes);
        Assert.Equal(new[] {
            "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9",
            "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106",
            "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"
        }, Hashes(Path.Combine(destinationRoot, "ISO")).Values.Order().ToArray());
        Assert.Equal(originals.OrderBy(p => p.Key), Hashes(sourceRoot).OrderBy(p => p.Key));
        var fractions = snapshots.Values.Where(s => s.Progress is not null).Select(s => s.Progress!.Fraction).ToArray();
        Assert.Equal(fractions.Order(), fractions);
        if (firstPassword == "wrong") Assert.True(interaction.Passwords >= 2);
    }

    [LinuxArchiveFileSystemTheory]
    [InlineData("skip")]
    [InlineData("duplicate")]
    [InlineData("conflict")]
    [InlineData("empty")]
    public async Task Linux_engine_enforces_real_archive_paths_and_conflicts(string scenario)
    {
        using var source = new CopyTestTree();
        using var destination = new CopyTestTree();
        using (var zip = ZipFile.Open(Path.Combine(source.Root, "game.zip"), ZipArchiveMode.Create))
        {
            if (scenario == "skip") zip.CreateEntry("../escape.iso");
            if (scenario is "duplicate" or "conflict")
            {
                using (var writer = new StreamWriter(zip.CreateEntry("nested/game.iso").Open())) writer.Write("first");
                using (var writer = new StreamWriter(zip.CreateEntry(scenario == "duplicate" ? "nested/game.iso" : "nested").Open())) writer.Write("second");
            }
        }
        var interaction = new Interaction();
        var result = await new GameCopyService(new RoutedFileSystem(source.FileSystem, destination.FileSystem),
            new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot), interaction).CopyAsync(Request(source.Evidence, destination.Evidence, "game.zip"));
        if (scenario == "skip")
        {
            Assert.Equal(GameCopyState.CompletedWithSkippedFiles, result.State);
            Assert.False(Directory.Exists(Path.Combine(destination.Root, "ISO")));
        }
        else if (scenario == "duplicate")
        {
            Assert.Equal(GameCopyState.Completed, result.State);
            Assert.Equal(1, interaction.Overwrites);
            Assert.Equal("second", File.ReadAllText(Path.Combine(destination.Root, "ISO/nested/game.iso")));
        }
        else if (scenario == "conflict")
        {
            Assert.Equal(GameCopyErrorCode.FileDirectoryConflict, result.Error!.Code);
            Assert.Equal("first", File.ReadAllText(Path.Combine(destination.Root, "ISO/nested/game.iso")));
        }
        else
        {
            Assert.Equal(GameCopyErrorCode.EmptyArchive, result.Error!.Code);
            Assert.False(Directory.Exists(Path.Combine(destination.Root, "ISO")));
        }
        Assert.Empty(Directory.GetFileSystemEntries(destination.Outside));
    }

    [LinuxArchiveFileSystemTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Encrypted_error_waits_with_closed_outputs_and_retains_bytes_on_cancel(bool damaged)
    {
        using var source = new CopyTestTree();
        using var destination = new CopyTestTree();
        Stage(source.Root);
        const string name = "Rar.encrypted_filesOnly.rar";
        if (damaged)
        {
            var path = Path.Combine(source.Root, name);
            var bytes = File.ReadAllBytes(path); bytes[50000] ^= 0x10; File.WriteAllBytes(path, bytes);
        }
        var original = Hashes(source.Root);
        var outputs = new List<Stream>();
        var destinationFs = destination.CreateFileSystem(new() { WrapOutput = stream => { outputs.Add(stream); return stream; } });
        var interaction = new Interaction { FirstPassword = damaged ? "test" : "wrong", CancelAfterFirstPassword = true };
        Dictionary<string, string>? retained = null;
        // The destination retains read-only identity pins; writable output streams must be closed.
        interaction.OnAmbiguous = () => {
            Assert.All(outputs, stream => Assert.False(stream.CanWrite));
            retained = Hashes(Path.Combine(destination.Root, "ISO"));
        };
        var snapshots = new Snapshots();
        var result = await new GameCopyService(new RoutedFileSystem(source.FileSystem, destinationFs),
            new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot), interaction)
            .CopyAsync(Request(source.Evidence, destination.Evidence, name), snapshots);
        Assert.True(result.State == GameCopyState.Cancelled, result.Error?.Message);
        Assert.Contains(snapshots.Values, s => s.State == GameCopyState.AwaitingPasswordDecision && s.Error!.Message == "Incorrect password or damaged archive");
        Assert.True(Directory.Exists(Path.Combine(destination.Root, "ISO")));
        Assert.NotNull(retained);
        Assert.Equal(retained.OrderBy(p => p.Key), Hashes(Path.Combine(destination.Root, "ISO")).OrderBy(p => p.Key));
        if (damaged) Assert.Contains(Directory.EnumerateFiles(Path.Combine(destination.Root, "ISO"), "*", SearchOption.AllDirectories), path => new FileInfo(path).Length > 0);
        Assert.Equal(original.OrderBy(p => p.Key), Hashes(source.Root).OrderBy(p => p.Key));
        Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(destination.Root));
    }

    [LinuxArchiveFileSystemTheory]
    [InlineData("Rar5.multi.part01.rar", "Rar5.multi.part02.rar")]
    [InlineData("WinZip26.nocomp.multi.zip", "WinZip26.nocomp.multi.z01")]
    public async Task Missing_part_fails_without_password_prompt(string first, string missing)
    {
        using var source = new CopyTestTree();
        using var destination = new CopyTestTree();
        Stage(source.Root); File.Delete(Path.Combine(source.Root, missing));
        var interaction = new Interaction();
        var result = await new GameCopyService(new RoutedFileSystem(source.FileSystem, destination.FileSystem),
            new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot), interaction).CopyAsync(Request(source.Evidence, destination.Evidence, first));
        Assert.Equal(GameCopyErrorCode.MissingPart, result.Error!.Code);
        Assert.Equal(0, interaction.Passwords);
    }

    [LinuxArchiveFileSystemTheory]
    [InlineData("Rar.encrypted_filesOnly.rar")]
    [InlineData("Rar5.encrypted_filesOnly.rar")]
    [InlineData("Zip.deflate.WinzipAES.zip")]
    public async Task Wrong_password_can_be_retried_through_native_engine(string name)
    {
        using var source = new CopyTestTree();
        using var destination = new CopyTestTree();
        await VerifyArchive(source.Root, destination.Root, source.Evidence, destination.Evidence,
            new RoutedFileSystem(source.FileSystem, destination.FileSystem), name, "wrong");
    }

    [LinuxArchiveFileSystemTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_transfer_cancellation_preserves_exact_written_prefix(bool overwrite)
    {
        using var source = new CopyTestTree();
        using var destination = new CopyTestTree();
        var payload = Enumerable.Range(0, 1048576).Select(i => (byte)(i % 251)).ToArray();
        using (var zip = ZipFile.Open(Path.Combine(source.Root, "game.zip"), ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("game.iso", CompressionLevel.NoCompression).Open()) entry.Write(payload);
        var originals = Hashes(source.Root);
        if (overwrite)
        {
            Directory.CreateDirectory(Path.Combine(destination.Root, "ISO"));
            File.WriteAllBytes(Path.Combine(destination.Root, "ISO/game.iso"), [255, 255]);
        }
        using var cancellation = new CancellationTokenSource();
        var snapshots = new Snapshots { OnReport = snapshot => {
            if (snapshot.Progress?.Work.WrittenBytes > 0 && !GameCopyRules.IsTerminal(snapshot.State)) cancellation.Cancel();
        } };
        var result = await new GameCopyService(new RoutedFileSystem(source.FileSystem, destination.FileSystem),
            new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot), new Interaction())
            .CopyAsync(Request(source.Evidence, destination.Evidence, "game.zip"), snapshots, cancellation.Token);
        Assert.Equal(GameCopyState.Cancelled, result.State);
        var retained = File.ReadAllBytes(Path.Combine(destination.Root, "ISO/game.iso"));
        Assert.NotEmpty(retained);
        Assert.Equal(payload.Take(retained.Length), retained);
        Assert.Equal(originals.OrderBy(p => p.Key), Hashes(source.Root).OrderBy(p => p.Key));
        Assert.DoesNotContain(snapshots.Values, s => s.Progress?.Fraction == 1);
        Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(destination.Root));
    }

    private static GameCopyRequest Request(CopyVolumeEvidence source, CopyVolumeEvidence destination, string name) => new(Guid.NewGuid(),
        new(Guid.NewGuid(), "Fixture", GameMedium.Usb, null, "/" + name, GameFormat.Iso, 1, null), source, destination);
    private static void Stage(string root)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers")))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
    }
    private static Dictionary<string, string> Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    private sealed class RoutedFileSystem(IGameCopyFileSystem source, IGameCopyFileSystem destination) : IGameCopyFileSystem
    {
        public ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence evidence, string location, CancellationToken token) => source.OpenSourceAsync(evidence, location, token);
        public ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence evidence, CancellationToken token) => destination.OpenDestinationAsync(evidence, token);
    }
    private sealed class Snapshots : IProgress<GameCopySnapshot>
    {
        internal List<GameCopySnapshot> Values { get; } = [];
        internal Action<GameCopySnapshot>? OnReport;
        public void Report(GameCopySnapshot value) { Values.Add(value); OnReport?.Invoke(value); }
    }
    private sealed class Interaction : IGameCopyInteraction
    {
        internal int Passwords, Overwrites;
        internal string FirstPassword = "test";
        internal bool CancelAfterFirstPassword;
        internal Action? OnAmbiguous;
        public ValueTask<CopyPasswordResponse> RequestPasswordAsync(CopyPasswordPrompt prompt, CancellationToken token)
        {
            Passwords++;
            if (prompt.Reason == GameCopyErrorCode.IndeterminateEncryptedError) OnAmbiguous?.Invoke();
            return ValueTask.FromResult(new CopyPasswordResponse(prompt.Identity,
                Passwords > 1 && CancelAfterFirstPassword ? CopyPasswordDecision.Cancel : CopyPasswordDecision.Retry,
                Passwords == 1 ? FirstPassword : "test"));
        }
        public ValueTask<CopyOverwriteResponse> ConfirmOverwriteAsync(CopyOverwritePrompt prompt, CancellationToken token)
        { Overwrites++; return ValueTask.FromResult(new CopyOverwriteResponse(prompt.Identity, CopyOverwriteDecision.Overwrite)); }
    }
}
