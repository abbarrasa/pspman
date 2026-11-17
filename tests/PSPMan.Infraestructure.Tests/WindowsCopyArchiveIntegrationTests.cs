using System.Collections.Immutable;
using System.Security.Cryptography;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsCopyArchiveIntegrationTests
{
    [WindowsCopyTheory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Rar.EncryptedParts.part02.rar", "test")]
    [InlineData("WinZip26.nocomp.multi.z01", null)]
    public async Task NativeReaderStreamsConfinedWindowsFixturesWithoutChangingSources(string name, string? password)
    {
        using var sourceArea = new WindowsCopyTestArea();
        using var destinationArea = new WindowsCopyTestArea();
        var sourceFolder = Path.Combine(sourceArea.Root, "archives");
        Directory.CreateDirectory(sourceFolder);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers")))
            File.Copy(file, Path.Combine(sourceFolder, Path.GetFileName(file)));
        var originals = Directory.EnumerateFiles(sourceFolder).ToDictionary(file => Path.GetFileName(file)!,
            file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
        await using (var source = await sourceArea.FileSystem.OpenSourceAsync(sourceArea.Evidence, "/archives/" + name, default))
        await using (var destination = await destinationArea.FileSystem.OpenDestinationAsync(destinationArea.Evidence, default))
        await using (var archive = await new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot).OpenAsync(source, password, default))
        {
            var info = await archive.InspectAsync(default);
            Assert.Equal(100926, GameCopyRules.SumActualBytes(info.Entries).Bytes);
            Assert.Null(GameCopyRules.CheckCapacity(100926, await destination.GetAvailableBytesAsync(default)));
            Assert.False(Directory.Exists(destination.DestinationPath));
            var target = new FileTarget(destination);
            await archive.ExtractAsync(info.Entries.Select(entry => entry.Index).ToImmutableArray(), target, null, default);
            Assert.Empty(target.Active);
        }
        Assert.Equal(new[] {
            "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9",
            "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106",
            "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"
        }, Directory.EnumerateFiles(Path.Combine(destinationArea.Root, "ISO"), "*", SearchOption.AllDirectories)
            .Select(file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))).Order().ToArray());
        foreach (var (file, expected) in originals)
            Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceFolder, file)))));
        Directory.Move(sourceArea.Root, sourceArea.Root + "-released");
        Directory.Move(destinationArea.Root, destinationArea.Root + "-released");
    }

    // Test-only target; production orchestration and user decisions remain T06.
    private sealed class FileTarget(IGameCopyDestination destination) : IGameArchiveExtractionTarget
    {
        internal Dictionary<int, IGameCopyOutputFile> Active { get; } = [];
        public async ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken token)
        {
            var path = GameCopyRules.ClassifyEntry(entry.Path, entry.IsLink, entry.IsDirectory);
            if (!path.IsSafe) return new(null, path.SkipReason);
            if (entry.IsDirectory) return new(null, await destination.EnsureDirectoryAsync(path.Segments, token));
            var result = await destination.OpenOutputAsync(path.Segments, null, token);
            Assert.False(result.TargetChanged);
            if (result.File is null) return new(null, result.SkipReason);
            Active.Add(entry.Index, result.File);
            return new(result.File.Output, null);
        }
        public async ValueTask CompleteEntryAsync(int index, GameCopyError? error)
        { if (Active.Remove(index, out var file)) await file.DisposeAsync(); }
    }
}

public sealed class WindowsCopyTheoryAttribute : TheoryAttribute
{
    public WindowsCopyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_WINDOWS_COPY_TESTS") == "1")
                throw new InvalidOperationException("Mandatory Windows copy tests require a native Windows runner.");
            Skip = "T05 requires native Windows execution; completion gate remains open.";
        }
    }
}
