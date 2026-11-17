using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Real Windows calls on dedicated temp roots; synthetic inventory is not PSP evidence.</summary>
public sealed class WindowsCopyFileAccessTests
{
    [WindowsCopyFact]
    public async Task HardLinkedExistingDestinationIsSkippedWithoutChangingTheOutsideName()
    {
        using var area = new WindowsCopyTestArea();
        Directory.CreateDirectory(Path.Combine(area.Root, "ISO"));
        var outside = Path.Combine(area.Outside, "safe");
        File.WriteAllText(outside, "untouched");
        WindowsCopyTestArea.CreateHardLink(Path.Combine(area.Root, "ISO", "game.iso"), outside);
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        Assert.False((await destination.InspectAsync(["game.iso"], default)).IsConfined);
        Assert.Equal(GameCopySkipReason.Link, (await destination.OpenOutputAsync(["game.iso"], null, default)).SkipReason);
        Assert.Equal("untouched", File.ReadAllText(outside));
    }

    [WindowsCopyFact]
    public async Task CapturedVolumeGuidMismatchFailsBeforeCreatingIso()
    {
        using var area = new WindowsCopyTestArea();
        var evidence = area.Evidence with { Volume = new(area.Evidence.Device, @"\\?\Volume{" + Guid.NewGuid() + @"}\") };
        var fs = new FileGameCopyFileSystem(_ => ValueTask.FromResult(new DeviceInventorySnapshot(1,
            [new(evidence.Device, "Fixture", DeviceMedium.Psp, DeviceCapabilities.PspDestination, evidence.DeviceIdentity,
                evidence.MediaGeneration, DeviceState.Ready, [new(evidence.Volume, null, evidence.Root, evidence.VolumeIdentity, DeviceState.Ready)])])));
        var error = await Assert.ThrowsAsync<GameCopyException>(async () => await fs.OpenDestinationAsync(evidence, default));
        Assert.Equal(GameCopyErrorCode.DestinationUnavailable, error.Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(area.Root));
    }

    [WindowsCopyFact]
    public async Task SameLengthModificationRequiresNewConsentAndIncludesAContentHash()
    {
        using var area = new WindowsCopyTestArea();
        Directory.CreateDirectory(Path.Combine(area.Root, "ISO"));
        var path = Path.Combine(area.Root, "ISO", "game.iso");
        File.WriteAllText(path, "one");
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        var consent = (await destination.InspectAsync(["game.iso"], default)).Evidence!;
        Assert.EndsWith(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), consent.ChangeEvidence);
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "two"); File.SetLastWriteTimeUtc(path, time);
        Assert.True((await destination.OpenOutputAsync(["game.iso"], consent, default)).TargetChanged);
        Assert.Equal("two", File.ReadAllText(path));
    }

    [WindowsCopyFact]
    public async Task MissingAndJunctionSourcePathsFailWithoutReadingForeignFiles()
    {
        using var area = new WindowsCopyTestArea();
        Assert.Equal(GameCopyErrorCode.MissingArchive, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await area.FileSystem.OpenSourceAsync(area.Evidence, "/missing.rar", default))).Error.Code);
        File.WriteAllText(Path.Combine(area.Outside, "game.rar"), "foreign");
        WindowsCopyTestArea.CreateJunction(Path.Combine(area.Root, "archives"), area.Outside);
        Assert.Equal(GameCopyErrorCode.ReadError, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await area.FileSystem.OpenSourceAsync(area.Evidence, "/archives/game.rar", default))).Error.Code);
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(area.Outside, "game.rar")));
    }

    [WindowsCopyFact]
    public async Task PreflightDoesNotCreateIsoAndQueriesSpaceThroughTheCapturedHandle()
    {
        using var area = new WindowsCopyTestArea();
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        Assert.True(await destination.GetAvailableBytesAsync(default) > 0);
        Assert.Equal(Path.Combine(area.Root, "ISO"), destination.DestinationPath);
        Assert.Equal(CopyTargetKind.Missing, (await destination.InspectAsync(["nested", "game.iso"], default)).Kind);
        Assert.Empty(Directory.GetFileSystemEntries(area.Root));
    }

    [WindowsCopyFact]
    public async Task NestedUnicodeAndEmptyDirectoriesPreserveBytesAndDuplicatesRequireConsent()
    {
        using var area = new WindowsCopyTestArea();
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        Assert.Null(await destination.EnsureDirectoryAsync(["empty"], default));
        var path = ImmutableArray.Create("日本語", "🎮.iso");
        await using (var file = (await destination.OpenOutputAsync(path, null, default)).File!)
            await file.Output.WriteAsync(new byte[] { 1, 2, 3 });
        Assert.True((await destination.OpenOutputAsync(path, null, default)).TargetChanged);
        var consent = (await destination.InspectAsync(path, default)).Evidence;
        await using (var file = (await destination.OpenOutputAsync(path, consent, default)).File!)
            await file.Output.WriteAsync(new byte[] { 9 });
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(area.Root, "ISO", "日本語", "🎮.iso")));
        Assert.True(Directory.Exists(Path.Combine(area.Root, "ISO", "empty")));
    }

    [WindowsCopyFact]
    public async Task TargetReplacementBeforeWriteRechecksConsentWithoutTruncatingEitherFile()
    {
        using var area = new WindowsCopyTestArea();
        Directory.CreateDirectory(Path.Combine(area.Root, "ISO"));
        var path = Path.Combine(area.Root, "ISO", "game.iso");
        File.WriteAllText(path, "original");
        var fs = area.CreateFileSystem(new() { BeforeMutation = (stage, _) =>
        {
            if (stage != "open") return;
            File.Move(path, Path.Combine(area.Root, "ISO", "retired"));
            File.WriteAllText(path, "replacement");
        }});
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        var consent = (await destination.InspectAsync(["game.iso"], default)).Evidence;
        Assert.True((await destination.OpenOutputAsync(["game.iso"], consent, default)).TargetChanged);
        Assert.Equal("replacement", File.ReadAllText(path));
        Assert.Equal("original", File.ReadAllText(Path.Combine(area.Root, "ISO", "retired")));
    }

    [WindowsCopyFact]
    public async Task AcquiredWriteAndParentHandlesDenyReplacementBeforeTruncation()
    {
        using var area = new WindowsCopyTestArea();
        var parent = Path.Combine(area.Root, "ISO", "nested");
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, "game.iso");
        File.WriteAllText(path, "original");
        bool attempted = false;
        var fs = area.CreateFileSystem(new() { BeforeMutation = (stage, _) =>
        {
            if (stage != "truncate") return;
            attempted = true;
            Assert.ThrowsAny<IOException>(() => File.Move(path, path + ".retired"));
            Assert.ThrowsAny<IOException>(() => Directory.Move(parent, parent + ".retired"));
        }});
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        var consent = (await destination.InspectAsync(["nested", "game.iso"], default)).Evidence;
        await using var output = (await destination.OpenOutputAsync(["nested", "game.iso"], consent, default)).File!;
        Assert.True(attempted);
        await output.Output.WriteAsync(new byte[] { 8 });
    }

    [WindowsCopyFact]
    public async Task FileAppearingDuringExclusiveCreateIsNotOverwritten()
    {
        using var area = new WindowsCopyTestArea();
        var path = Path.Combine(area.Root, "ISO", "game.iso");
        var fs = area.CreateFileSystem(new() { BeforeMutation = (stage, _) => { if (stage == "open") File.WriteAllText(path, "concurrent"); } });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        Assert.True((await destination.OpenOutputAsync(["game.iso"], null, default)).TargetChanged);
        Assert.Equal("concurrent", File.ReadAllText(path));
    }

    [WindowsCopyFact]
    public async Task JunctionAndFileSymlinkEntriesAreSkippedWithoutOutsideWrites()
    {
        using var area = new WindowsCopyTestArea();
        Directory.CreateDirectory(Path.Combine(area.Root, "ISO"));
        WindowsCopyTestArea.CreateJunction(Path.Combine(area.Root, "ISO", "junction"), area.Outside);
        var outside = Path.Combine(area.Outside, "safe");
        File.WriteAllText(outside, "untouched");
        File.CreateSymbolicLink(Path.Combine(area.Root, "ISO", "link"), outside);
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        foreach (var path in new[] { ImmutableArray.Create("junction", "game.iso"), ["link"] })
        {
            Assert.False((await destination.InspectAsync(path, default)).IsConfined);
            Assert.Equal(GameCopySkipReason.Link, (await destination.OpenOutputAsync(path, null, default)).SkipReason);
        }
        Assert.Equal(GameCopySkipReason.Link, await destination.EnsureDirectoryAsync(["junction", "empty"], default));
        Assert.Equal("untouched", File.ReadAllText(outside));
        Assert.Single(Directory.GetFileSystemEntries(area.Outside));
    }

    [WindowsCopyFact]
    public async Task LateJunctionInsertionDuringDirectoryCreationIsSkipped()
    {
        using var area = new WindowsCopyTestArea();
        var fs = area.CreateFileSystem(new() { BeforeMutation = (stage, relative) =>
        { if (stage == "directory" && relative == "ISO") WindowsCopyTestArea.CreateJunction(Path.Combine(area.Root, "ISO"), area.Outside); } });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        Assert.Equal(GameCopySkipReason.Link, await destination.EnsureDirectoryAsync(["nested"], default));
        Assert.Empty(Directory.GetFileSystemEntries(area.Outside));
    }

    [WindowsCopyFact]
    public async Task NativeCaseLookupAndEnabledDirectoryCaseSensitivityArePreserved()
    {
        using var area = new WindowsCopyTestArea();
        var iso = Path.Combine(area.Root, "ISO");
        Directory.CreateDirectory(iso);
        File.WriteAllText(Path.Combine(iso, "Game.iso"), "ordinary");
        await using (var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default))
        {
            var aliases = File.Exists(Path.Combine(iso, "game.iso"));
            Assert.Equal(aliases ? CopyTargetKind.File : CopyTargetKind.Missing, (await destination.InspectAsync(["game.iso"], default)).Kind);
        }
        File.Delete(Path.Combine(iso, "Game.iso"));
        WindowsCopyTestArea.EnableCaseSensitivity(iso); // dedicated NTFS fixture; failure remains a missing native prerequisite
        File.WriteAllText(Path.Combine(iso, "Game.iso"), "sensitive");
        await using var sensitive = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        Assert.Equal(CopyTargetKind.File, (await sensitive.InspectAsync(["Game.iso"], default)).Kind);
        Assert.Equal(CopyTargetKind.Missing, (await sensitive.InspectAsync(["game.iso"], default)).Kind);
    }

    [WindowsCopyFact]
    public async Task InvalidNamesStructuralConflictsAndUnsafePathsCannotMutateOutsideIso()
    {
        using var area = new WindowsCopyTestArea();
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        Assert.Equal(GameCopySkipReason.UnsafePath, (await destination.OpenOutputAsync(["..", "escape"], null, default)).SkipReason);
        Assert.Equal(GameCopyErrorCode.InvalidName, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await destination.OpenOutputAsync(["CON.iso"], null, default))).Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(area.Root));
        await destination.EnsureDirectoryAsync(["directory"], default);
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await destination.OpenOutputAsync(["directory"], null, default))).Error.Code);
        await using (var file = (await destination.OpenOutputAsync(["file"], null, default)).File!)
            await file.Output.WriteAsync(new byte[] { 1 });
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await destination.EnsureDirectoryAsync(["file", "nested"], default))).Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(area.Outside));
    }

    [WindowsCopyFact]
    public async Task ChangedVolumeBeforeTruncationRetainsOriginalAndBeforeWritesRetainsPartialOutput()
    {
        using var area = new WindowsCopyTestArea();
        Directory.CreateDirectory(Path.Combine(area.Root, "ISO"));
        var path = Path.Combine(area.Root, "ISO", "game.iso");
        File.WriteAllText(path, "original");
        var fs = area.CreateFileSystem(new() { BeforeMutation = (stage, _) => { if (stage == "truncate") area.Generation = "replacement"; } });
        await using var destination = await fs.OpenDestinationAsync(area.Evidence, default);
        var consent = (await destination.InspectAsync(["game.iso"], default)).Evidence;
        Assert.Equal(GameCopyErrorCode.DestinationChanged, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await destination.OpenOutputAsync(["game.iso"], consent, default))).Error.Code);
        Assert.Equal("original", File.ReadAllText(path));
        area.Generation = "initial";
        await using var file = (await destination.OpenOutputAsync(["partial.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1, 2 });
        area.Generation = "replacement";
        Assert.Equal(GameCopyErrorCode.DestinationChanged, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await file.Output.WriteAsync(new byte[] { 3 }))).Error.Code);
        await file.DisposeAsync();
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(area.Root, "ISO", "partial.iso")));
    }

    [WindowsCopyFact]
    public async Task SourcePartsAreReadOnlyConfinedAndClosedBySessionDisposal()
    {
        using var area = new WindowsCopyTestArea();
        Directory.CreateDirectory(Path.Combine(area.Root, "archives"));
        File.WriteAllText(Path.Combine(area.Root, "archives", "game.part02.rar"), "source");
        File.WriteAllText(Path.Combine(area.Root, "archives", "game.part01.rar"), "first");
        await using var source = await area.FileSystem.OpenSourceAsync(area.Evidence, "/archives/game.part02.rar", default);
        var token = await source.CaptureEvidenceAsync(default);
        var part = await source.OpenPartAsync("game.part01.rar", default);
        Assert.False(part.CanWrite);
        Assert.True(part.CanSeek);
        Assert.Equal("first", await new StreamReader(part, leaveOpen: true).ReadToEndAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.OpenPartAsync("game.part03.rar", default));
        await Assert.ThrowsAsync<GameCopyException>(async () => await source.OpenPartAsync("../escape.rar", default));
        await source.ValidateAsync(token, default);
        await source.DisposeAsync();
        await source.DisposeAsync();
        Assert.False(part.CanRead);
        Directory.Move(Path.Combine(area.Root, "archives"), Path.Combine(area.Root, "released"));
    }

    [WindowsCopyFact]
    public async Task SessionDisposalReleasesDirectoryRenameBarriersAndUnreleasedOutputStreams()
    {
        using var area = new WindowsCopyTestArea();
        await using var destination = await area.FileSystem.OpenDestinationAsync(area.Evidence, default);
        var file = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1 });
        Assert.ThrowsAny<IOException>(() => Directory.Move(area.Root, area.Root + "-moved"));
        await destination.DisposeAsync();
        await destination.DisposeAsync();
        Assert.False(file.Output.CanWrite);
        Directory.Move(area.Root, area.Root + "-moved");
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(area.Root + "-moved", "ISO", "game.iso")));
    }
}

internal sealed class WindowsCopyTestArea : IDisposable
{
    private readonly string _container = Path.Combine(Path.GetTempPath(), "pspman-win-copy-" + Guid.NewGuid().ToString("N"));
    internal string Root { get; }
    internal string Outside { get; }
    internal string Generation { get; set; } = "initial";
    internal CopyVolumeEvidence Evidence { get; }
    internal FileGameCopyFileSystem FileSystem { get; }
    internal WindowsCopyTestArea()
    {
        Root = Path.Combine(_container, "volume"); Outside = Path.Combine(_container, "volume-sibling");
        Directory.CreateDirectory(Root); Directory.CreateDirectory(Outside);
        var key = new DeviceKey("test", "windows-copy-fixture");
        var identity = new DeviceIdentity("fixture", DeviceIdentityConfidence.Persistent);
        Evidence = new(key, identity, Generation, new(key, WindowsCopyNative.ResolveMount(Root).VolumePath), identity, Root, MountedPathSyntax.Windows);
        FileSystem = CreateFileSystem();
    }
    internal FileGameCopyFileSystem CreateFileSystem(CopyFileAccessHooks? hooks = null) => new(_ => ValueTask.FromResult(new DeviceInventorySnapshot(1,
        [new(Evidence.Device, "Fixture", DeviceMedium.Psp, DeviceCapabilities.PspDestination | DeviceCapabilities.RemovableSource,
            Evidence.DeviceIdentity, Generation, DeviceState.Ready, [new(Evidence.Volume, null, Root, Evidence.VolumeIdentity, DeviceState.Ready)])])), hooks ?? new());
    public void Dispose() => Directory.Delete(_container, recursive: true);

    internal static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(link);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + Path.GetFullPath(target));
        var printable = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
        var buffer = new byte[16 + substitute.Length + 2 + printable.Length + 2];
        BitConverter.GetBytes(0xa0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes(checked((ushort)(buffer.Length - 8))).CopyTo(buffer, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(buffer, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(buffer, 12);
        BitConverter.GetBytes(checked((ushort)printable.Length)).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16); printable.CopyTo(buffer, 16 + substitute.Length + 2);
        using var handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x900a4, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
    internal static void EnableCaseSensitivity(string path)
    {
        using var handle = CreateFile(path, 0x180, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        uint flags = 1;
        if (handle.IsInvalid || !SetCaseInformation(handle, 23, ref flags, 4))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
    internal static void CreateHardLink(string link, string existing)
    {
        if (!CreateHardLinkNative(link, existing, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkNative(string link, string existing, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputLength, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCaseInformation(SafeFileHandle handle, int infoClass, ref uint flags, uint size);
}

public sealed class WindowsCopyFactAttribute : FactAttribute
{
    public WindowsCopyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_WINDOWS_COPY_TESTS") == "1")
                throw new InvalidOperationException("Mandatory Windows copy tests require a native Windows runner.");
            Skip = "T05 requires native Windows execution; completion gate remains open.";
        }
    }
}
