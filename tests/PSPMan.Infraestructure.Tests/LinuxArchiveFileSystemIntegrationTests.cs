using System.Collections.Immutable;
using System.Security.Cryptography;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxArchiveFileSystemIntegrationTests
{
    [LinuxArchiveFileSystemTheory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Rar.EncryptedParts.part02.rar", "test")]
    [InlineData("WinZip26.nocomp.multi.z01", null)]
    public async Task NativeReaderAndConfinedSourceDestinationPreserveFixtureBytes(string name, string? password)
    {
        using var sourceTree = new CopyTestTree();
        using var destinationTree = new CopyTestTree();
        var archiveDirectory = Path.Combine(sourceTree.Root, "archives");
        Directory.CreateDirectory(archiveDirectory);
        foreach (var fixture in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers")))
            File.Copy(fixture, Path.Combine(archiveDirectory, Path.GetFileName(fixture)));
        var before = Directory.EnumerateFiles(archiveDirectory).ToDictionary(path => Path.GetFileName(path)!,
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

        await using (var source = await sourceTree.FileSystem.OpenSourceAsync(sourceTree.Evidence, "/archives/" + name, default))
        await using (var destination = await destinationTree.FileSystem.OpenDestinationAsync(destinationTree.Evidence, default))
        await using (var archive = await new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot).OpenAsync(source, password, default))
        {
            var inspection = await archive.InspectAsync(default);
            var size = GameCopyRules.SumActualBytes(inspection.Entries);
            Assert.Equal(100926, size.Bytes);
            Assert.Null(GameCopyRules.CheckCapacity(size.Bytes!.Value, await destination.GetAvailableBytesAsync(default)));
            Assert.False(Directory.Exists(destination.DestinationPath));
            var target = new FileTarget(destination);
            await archive.ExtractAsync(inspection.Entries.Select(item => item.Index).ToImmutableArray(), target, null, default);
            Assert.Equal(0, target.OpenFiles);
        }
        var hashes = Directory.EnumerateFiles(Path.Combine(destinationTree.Root, "ISO"), "*", SearchOption.AllDirectories)
            .Select(path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).Order().ToArray();
        Assert.Equal(new[] {
            "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9",
            "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106",
            "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"
        }, hashes);
        foreach (var (file, hash) in before)
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(archiveDirectory, file!)))));
        Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(sourceTree.Root));
        Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(destinationTree.Root));
    }

    // Test-only extraction target; the production service and user prompts belong to T06.
    private sealed class FileTarget(IGameCopyDestination destination) : IGameArchiveExtractionTarget
    {
        private readonly Dictionary<int, IGameCopyOutputFile> _files = [];
        public int OpenFiles => _files.Count;
        public async ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken token)
        {
            var path = GameCopyRules.ClassifyEntry(entry.Path, entry.IsLink, entry.IsDirectory);
            if (!path.IsSafe) return new(null, path.SkipReason);
            if (entry.IsDirectory) return new(null, await destination.EnsureDirectoryAsync(path.Segments, token));
            var result = await destination.OpenOutputAsync(path.Segments, null, token);
            Assert.False(result.TargetChanged);
            if (result.File is null) return new(null, result.SkipReason);
            _files.Add(entry.Index, result.File);
            return new(result.File.Output, null);
        }
        public async ValueTask CompleteEntryAsync(int entryIndex, GameCopyError? error)
        { if (_files.Remove(entryIndex, out var file)) await file.DisposeAsync(); }
    }
}

public sealed class LinuxArchiveFileSystemTheoryAttribute : TheoryAttribute
{
    public LinuxArchiveFileSystemTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Requires native Linux filesystem access.";
        else if (!File.Exists(Path.Combine(GameArchiveReaderTests.RuntimeRoot, "runtimes", ArchiveRuntimeResolver.CurrentRuntimeIdentifier, "native", "runtime.json"))
            && Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_NATIVE_TESTS") != "1")
            Skip = "Requires staged native archive runtime; mandatory native gate remains pending.";
    }
}
