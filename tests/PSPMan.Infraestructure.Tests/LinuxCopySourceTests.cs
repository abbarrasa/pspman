using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxCopySourceTests
{
    [LinuxCopyFact]
    public async Task MissingArchiveAndMissingOptionalPartRemainDistinct()
    {
        using var tree = new CopyTestTree();
        var error = await Assert.ThrowsAsync<GameCopyException>(async () => await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/missing.rar", default));
        Assert.Equal(GameCopyErrorCode.MissingArchive, error.Error.Code);
        File.WriteAllText(Path.Combine(tree.Root, "game.rar"), "fixture");
        await using var source = await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/game.rar", default);
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.OpenPartAsync("game.r00", default));
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(tree.Root, "game.rar")));
    }

    [LinuxCopyTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceParentAndPartLinksAreRejected(bool parentLink)
    {
        using var tree = new CopyTestTree();
        File.WriteAllText(Path.Combine(tree.Outside, "game.rar"), "outside");
        if (parentLink) Directory.CreateSymbolicLink(Path.Combine(tree.Root, "archives"), tree.Outside);
        else
        {
            Directory.CreateDirectory(Path.Combine(tree.Root, "archives"));
            File.CreateSymbolicLink(Path.Combine(tree.Root, "archives", "game.rar"), Path.Combine(tree.Outside, "game.rar"));
        }
        var error = await Assert.ThrowsAsync<GameCopyException>(async () => await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/archives/game.rar", default));
        Assert.Equal(GameCopyErrorCode.ReadError, error.Error.Code);
        Assert.Equal("outside", File.ReadAllText(Path.Combine(tree.Outside, "game.rar")));
    }

    [LinuxCopyFact]
    public async Task SourceCannotEscapeToSiblingThroughCatalogLocation()
    {
        using var tree = new CopyTestTree();
        await Assert.ThrowsAsync<GameCopyException>(async () => await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/../volume-sibling/game.zip", default));
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyFact]
    public async Task ChangedOpenSourceStopsReadingAndCannotWrite()
    {
        using var tree = new CopyTestTree();
        var path = Path.Combine(tree.Root, "game.rar");
        File.WriteAllText(path, "original");
        await using var source = await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/game.rar", default);
        await using var input = await source.OpenPartAsync("game.rar", default);
        Assert.Throws<NotSupportedException>(() => input.WriteByte(1));
        File.WriteAllText(path, "changed-size");
        Assert.Equal(GameCopyErrorCode.SourceChanged,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await input.ReadExactlyAsync(new byte[1]))).Error.Code);
    }

    [LinuxCopyFact]
    public async Task SessionsOwnAndCloseAllFileAndConsentHandlesWithoutDeletingOutput()
    {
        using var tree = new CopyTestTree();
        var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        await output.Output.WriteAsync(new byte[] { 1, 2 });
        await destination.InspectAsync(["game.iso"], default);
        Assert.True(OpenDescriptorsBelow(tree.Root) >= 2);
        await destination.DisposeAsync();
        await destination.DisposeAsync();
        Assert.False(output.Output.CanWrite);
        Assert.Equal(0, OpenDescriptorsBelow(tree.Root));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));

        File.WriteAllText(Path.Combine(tree.Root, "game.rar"), "archive");
        var source = await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/game.rar", default);
        var part = await source.OpenPartAsync("game.rar", default);
        Assert.True(OpenDescriptorsBelow(tree.Root) >= 2);
        await source.DisposeAsync();
        Assert.False(part.CanRead);
        Assert.Equal(0, OpenDescriptorsBelow(tree.Root));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await source.CaptureEvidenceAsync(default));
    }

    [LinuxCopyFact]
    public async Task CancellationBeforeOpeningAndLateDirectoryLinkReturnWithoutWriting()
    {
        using var tree = new CopyTestTree();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await tree.FileSystem.OpenDestinationAsync(tree.Evidence, cancellation.Token));
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (stage, relative) =>
        {
            if (stage == "directory" && relative == "ISO") Directory.CreateSymbolicLink(Path.Combine(tree.Root, "ISO"), tree.Outside);
        }});
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(GameCopySkipReason.Link, await destination.EnsureDirectoryAsync(["nested"], default));
        Assert.Empty(Directory.GetFileSystemEntries(tree.Outside));
    }

    internal static int OpenDescriptorsBelow(string root)
    {
        int count = 0;
        foreach (var descriptor in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            try
            {
                var path = new FileInfo(descriptor).LinkTarget;
                if (path == root || path?.StartsWith(root + "/", StringComparison.Ordinal) == true) count++;
            }
            catch (IOException) { } // The enumerator's own descriptor may have closed.
        }
        return count;
    }
}
