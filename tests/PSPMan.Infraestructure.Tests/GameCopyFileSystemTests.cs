using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class GameCopyFileSystemTests
{
    [LinuxCopyFact]
    public async Task IsoRootEntryAndFlushUseTheBoundVolumeWithoutAcceptingEmptyFileNames()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Null(await destination.EnsureDirectoryAsync(default, default));
        Assert.True(Directory.Exists(Path.Combine(tree.Root, "ISO")));
        Assert.Equal(GameCopySkipReason.UnsafePath, (await destination.OpenOutputAsync([], null, default)).SkipReason);
        await using var file = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        file.Output.WriteByte(1);
        file.Output.Flush();
        await file.Output.FlushAsync();
        tree.Generation = "changed";
        Assert.Equal(GameCopyErrorCode.DestinationChanged,
            (await Assert.ThrowsAsync<GameCopyException>(() => file.Output.FlushAsync())).Error.Code);
        await file.DisposeAsync();
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task PreflightDoesNotCreateIsoAndReportsActualVolumeSpace()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(Path.Combine(tree.Root, "ISO"), destination.DestinationPath);
        Assert.True(await destination.GetAvailableBytesAsync(default) > 0);
        Assert.Equal(CopyTargetKind.Missing, (await destination.InspectAsync(["nested", "game.iso"], default)).Kind);
        Assert.False(Directory.Exists(destination.DestinationPath));
    }

    [LinuxCopyFact]
    public async Task NestedFilesDirectoriesAndDuplicateRequireExactConsent()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await destination.EnsureDirectoryAsync(["empty"], default);
        var opened = await destination.OpenOutputAsync(["nested", "game.iso"], null, default);
        await using (var file = Assert.IsAssignableFrom<IGameCopyOutputFile>(opened.File))
            await file.Output.WriteAsync(new byte[] { 1, 2, 3 });
        Assert.True(Directory.Exists(Path.Combine(tree.Root, "ISO", "empty")));
        Assert.True((await destination.OpenOutputAsync(["nested", "game.iso"], null, default)).TargetChanged);
        var consent = (await destination.InspectAsync(["nested", "game.iso"], default)).Evidence;
        var replacement = await destination.OpenOutputAsync(["nested", "game.iso"], consent, default);
        await using (var file = Assert.IsAssignableFrom<IGameCopyOutputFile>(replacement.File))
            await file.Output.WriteAsync(new byte[] { 9 });
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "nested", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task ChangedFileOrParentInvalidatesConsentWithoutTruncation()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO", "dir"));
        var path = Path.Combine(tree.Root, "ISO", "dir", "game.iso");
        File.WriteAllText(path, "old");
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var consent = (await destination.InspectAsync(["dir", "game.iso"], default)).Evidence;
        File.WriteAllText(path, "changed");
        Assert.True((await destination.OpenOutputAsync(["dir", "game.iso"], consent, default)).TargetChanged);
        Assert.Equal("changed", File.ReadAllText(path));
        consent = (await destination.InspectAsync(["dir", "game.iso"], default)).Evidence;
        Directory.Move(Path.Combine(tree.Root, "ISO", "dir"), Path.Combine(tree.Root, "ISO", "retired"));
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO", "dir"));
        File.Move(Path.Combine(tree.Root, "ISO", "retired", "game.iso"), path);
        Assert.True((await destination.OpenOutputAsync(["dir", "game.iso"], consent, default)).TargetChanged);
        Assert.Equal("changed", File.ReadAllText(path));
    }

    [LinuxCopyTheory]
    [InlineData("..")]
    [InlineData("/outside")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task UnsafeSegmentsNeverWrite(string segment)
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(GameCopySkipReason.UnsafePath,
            (await destination.OpenOutputAsync([segment], null, default)).SkipReason);
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyFact]
    public async Task ParentAndFinalLinksAreSkippedWithoutOutsideWrites()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        Directory.CreateSymbolicLink(Path.Combine(tree.Root, "ISO", "parent"), tree.Outside);
        File.WriteAllText(Path.Combine(tree.Outside, "safe"), "untouched");
        File.CreateSymbolicLink(Path.Combine(tree.Root, "ISO", "final"), Path.Combine(tree.Outside, "safe"));
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        foreach (var segments in new[] { ImmutableArray.Create("parent", "game.iso"), ["final"] })
        {
            Assert.False((await destination.InspectAsync(segments, default)).IsConfined);
            Assert.Equal(GameCopySkipReason.Link, (await destination.OpenOutputAsync(segments, null, default)).SkipReason);
        }
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(tree.Outside, "safe")));
        Assert.Single(Directory.GetFileSystemEntries(tree.Outside));
    }

    [LinuxCopyFact]
    public async Task FileDirectoryConflictAndInvalidNameFail()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO", "dir"));
        File.WriteAllText(Path.Combine(tree.Root, "ISO", "file"), "keep");
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["dir"], null, default))).Error.Code);
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.EnsureDirectoryAsync(["file"], default))).Error.Code);
        Assert.Equal(GameCopyErrorCode.InvalidName,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["bad\0name"], null, default))).Error.Code);
    }

    [LinuxCopyFact]
    public async Task SourcePartsAreReadOnlyAndConfinedToArchiveDirectory()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "archives"));
        File.WriteAllText(Path.Combine(tree.Root, "archives", "game.part01.rar"), "one");
        File.WriteAllText(Path.Combine(tree.Root, "archives", "game.part02.rar"), "two");
        await using var source = await tree.FileSystem.OpenSourceAsync(tree.Evidence, "/archives/game.part02.rar", default);
        var evidence = await source.CaptureEvidenceAsync(default);
        await using (var part = await source.OpenPartAsync("game.part01.rar", default))
        {
            Assert.False(part.CanWrite);
            Assert.True(part.CanSeek);
            Assert.Equal("one", await new StreamReader(part, leaveOpen: true).ReadToEndAsync());
        }
        await Assert.ThrowsAsync<GameCopyException>(async () => await source.OpenPartAsync("../escape.rar", default));
        await source.ValidateAsync(evidence, default);
        File.WriteAllText(Path.Combine(tree.Root, "archives", "game.part02.rar"), "new-size");
        Assert.Equal(GameCopyErrorCode.SourceChanged,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await source.ValidateAsync(evidence, default))).Error.Code);
    }

    [LinuxCopyFact]
    public async Task ChangedVolumeOrRootStopsWritesAndRetainsBytes()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        await output.Output.WriteAsync(new byte[] { 1, 2 });
        tree.Generation = "replacement";
        Assert.Equal(GameCopyErrorCode.DestinationChanged,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await output.Output.WriteAsync(new byte[] { 3 }))).Error.Code);
        await output.DisposeAsync();
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task CaseLookupUsesTheActualFilesystem()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        File.WriteAllText(Path.Combine(tree.Root, "ISO", "Game.iso"), "keep");
        bool aliases = File.Exists(Path.Combine(tree.Root, "ISO", "game.iso"));
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(aliases ? CopyTargetKind.File : CopyTargetKind.Missing,
            (await destination.InspectAsync(["game.iso"], default)).Kind);
    }
}

internal sealed class CopyTestTree : IDisposable
{
    private readonly string _container = Path.Combine(Path.GetTempPath(), "pspman-copy-" + Guid.NewGuid().ToString("N"));
    public string Root { get; }
    public string Outside { get; }
    public string Generation { get; set; } = "initial";
    public Func<DeviceInventorySnapshot, DeviceInventorySnapshot>? ModifyInventory { get; set; }
    public CopyVolumeEvidence Evidence { get; }
    public FileGameCopyFileSystem FileSystem { get; }
    public CopyTestTree()
    {
        Root = Path.Combine(_container, "volume");
        Outside = Path.Combine(_container, "volume-sibling");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Outside);
        var device = new DeviceKey("test", "selected-device");
        var identity = new DeviceIdentity("fixture", DeviceIdentityConfidence.Persistent);
        Evidence = new(device, identity, Generation, new(device, "selected-volume"), identity, Root, MountedPathSyntax.Linux);
        FileSystem = CreateFileSystem();
    }
    public FileGameCopyFileSystem CreateFileSystem(CopyFileAccessHooks? hooks = null) =>
        new(_ => ValueTask.FromResult(ModifyInventory?.Invoke(Inventory()) ?? Inventory()), hooks ?? new());
    public DeviceInventorySnapshot Inventory() => new(1, [new(Evidence.Device, "Fixture", DeviceMedium.Psp,
        DeviceCapabilities.PspDestination | DeviceCapabilities.RemovableSource, Evidence.DeviceIdentity,
        Generation, DeviceState.Ready, [new(Evidence.Volume, null, Root, Evidence.VolumeIdentity, DeviceState.Ready)])]);
    public void Dispose() => Directory.Delete(_container, recursive: true);
}
internal sealed class LinuxCopyFactAttribute : FactAttribute
{
    public LinuxCopyFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires native Linux filesystem access."; }
}
internal sealed class LinuxCopyTheoryAttribute : TheoryAttribute
{
    public LinuxCopyTheoryAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires native Linux filesystem access."; }
}
