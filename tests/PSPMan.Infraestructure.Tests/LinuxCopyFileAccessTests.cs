using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxCopyFileAccessTests
{
    [LinuxCopyFact]
    public async Task LateAncestorLinkDuringDirectoryCreationIsSkipped()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (stage, relative) =>
        {
            if (stage != "directory" || relative != "ISO/nested") return;
            Directory.Move(Path.Combine(tree.Root, "ISO"), Path.Combine(tree.Root, "retired"));
            Directory.CreateSymbolicLink(Path.Combine(tree.Root, "ISO"), tree.Outside);
        }});
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(GameCopySkipReason.Link, await destination.EnsureDirectoryAsync(["nested", "empty"], default));
        Assert.Empty(Directory.GetFileSystemEntries(tree.Outside));
    }

    [LinuxCopyFact]
    public async Task HardLinkedOutputIsSkippedWithoutChangingItsOtherName()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        var outside = Path.Combine(tree.Outside, "keep");
        File.WriteAllText(outside, "untouched");
        Assert.Equal(0, Link(outside, Path.Combine(tree.Root, "ISO", "game.iso")));
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.False((await destination.InspectAsync(["game.iso"], default)).IsConfined);
        Assert.Equal(GameCopySkipReason.Link, (await destination.OpenOutputAsync(["game.iso"], null, default)).SkipReason);
        Assert.Equal("untouched", File.ReadAllText(outside));
    }

    [LinuxCopyFact]
    public void NativeOpenRejectsAnActualNestedProcMount()
    {
        using var root = LinuxCopyNative.TryOpen(-100, "/", LinuxCopyNative.Directory, out var error, root: true);
        Assert.NotNull(root);
        using var proc = LinuxCopyNative.TryOpen(root.DangerousGetHandle().ToInt32(), "proc", LinuxCopyNative.PathOnly, out error);
        Assert.Null(proc);
        Assert.Equal(LinuxCopyNative.CrossDevice, error);
    }

    [LinuxCopyFact]
    public async Task UnicodeNamesRoundTripAndMalformedUnicodeIsRejected()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var file = (await destination.OpenOutputAsync(["日本語", "🎮.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1 });
        Assert.True(File.Exists(Path.Combine(tree.Root, "ISO", "日本語", "🎮.iso")));
        Assert.Equal(GameCopyErrorCode.InvalidName,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["bad\ud800.iso"], null, default))).Error.Code);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string existing,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string created);

    [LinuxCopyFact]
    public async Task OverwriteEvidenceContainsContentHashAndRepeatedInspectionDoesNotLeakPins()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        var path = Path.Combine(tree.Root, "ISO", "game.iso");
        File.WriteAllText(path, "one");
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var first = (await destination.InspectAsync(["game.iso"], default)).Evidence!;
        Assert.EndsWith(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), first.ChangeEvidence);
        var descriptors = LinuxCopySourceTests.OpenDescriptorsBelow(tree.Root);
        for (int i = 0; i < 20; i++) Assert.Equal(first, (await destination.InspectAsync(["game.iso"], default)).Evidence);
        Assert.Equal(descriptors, LinuxCopySourceTests.OpenDescriptorsBelow(tree.Root));
        var modificationTime = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "two");
        File.SetLastWriteTimeUtc(path, modificationTime);
        Assert.True((await destination.OpenOutputAsync(["game.iso"], first, default)).TargetChanged);
        Assert.Equal("two", File.ReadAllText(path));
    }

    [LinuxCopyTheory]
    [InlineData("open")]
    [InlineData("truncate")]
    public async Task ReplacementAtMutationBoundaryCannotTruncateEitherFile(string stage)
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        var path = Path.Combine(tree.Root, "ISO", "game.iso");
        File.WriteAllText(path, "old contents");
        bool fired = false;
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (point, _) =>
        {
            if (point != stage || fired) return;
            fired = true;
            File.Move(path, Path.Combine(tree.Root, "ISO", "retired"));
            File.WriteAllText(path, "replacement");
        }});
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        var consent = (await destination.InspectAsync(["game.iso"], default)).Evidence;
        Assert.True((await destination.OpenOutputAsync(["game.iso"], consent, default)).TargetChanged);
        Assert.True(fired);
        Assert.Equal("replacement", File.ReadAllText(path));
        Assert.Equal("old contents", File.ReadAllText(Path.Combine(tree.Root, "ISO", "retired")));
    }

    [LinuxCopyTheory]
    [InlineData("open")]
    [InlineData("truncate")]
    [InlineData("directory")]
    public async Task ParentSwapToSiblingLinkNeverWritesOutsideIso(string stage)
    {
        using var tree = new CopyTestTree();
        var parent = Path.Combine(tree.Root, "ISO", "dir");
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        if (stage != "directory")
        {
            Directory.CreateDirectory(parent);
            File.WriteAllText(Path.Combine(parent, "game.iso"), "keep");
        }
        bool fired = false;
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (point, relative) =>
        {
            if (point != stage || fired || stage == "directory" && relative != "ISO/dir") return;
            fired = true;
            if (Directory.Exists(parent)) Directory.Move(parent, Path.Combine(tree.Root, "ISO", "retired"));
            Directory.CreateSymbolicLink(parent, tree.Outside);
        }});
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        var consent = (await destination.InspectAsync(["dir", "game.iso"], default)).Evidence;
        Assert.Equal(GameCopySkipReason.Link, (await destination.OpenOutputAsync(["dir", "game.iso"], consent, default)).SkipReason);
        Assert.True(fired);
        Assert.Empty(Directory.GetFileSystemEntries(tree.Outside));
        if (stage != "directory") Assert.Equal("keep", File.ReadAllText(Path.Combine(tree.Root, "ISO", "retired", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task ExclusiveCreateCollisionReturnsChangedWithoutTouchingNewFile()
    {
        using var tree = new CopyTestTree();
        var path = Path.Combine(tree.Root, "ISO", "game.iso");
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (point, _) =>
        { if (point == "open") File.WriteAllText(path, "concurrent file"); } });
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.True((await destination.OpenOutputAsync(["game.iso"], null, default)).TargetChanged);
        Assert.Equal("concurrent file", File.ReadAllText(path));
    }

    [LinuxCopyFact]
    public async Task ReplacedRootCannotRedirectAnOpenOutputToAnotherDirectory()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var file = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1 });
        var retired = tree.Root + "-retired";
        Directory.Move(tree.Root, retired);
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        File.WriteAllText(Path.Combine(tree.Root, "ISO", "game.iso"), "replacement");
        Assert.Equal(GameCopyErrorCode.DestinationChanged,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await file.Output.WriteAsync(new byte[] { 2 }))).Error.Code);
        await file.DisposeAsync();
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(retired, "ISO", "game.iso")));
        Assert.Equal("replacement", File.ReadAllText(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyTheory]
    [InlineData("identity")]
    [InlineData("volume")]
    [InlineData("root")]
    [InlineData("state")]
    public async Task FreshProviderEvidenceRejectsMismatchedIdentityVolumeRootOrState(string change)
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        tree.ModifyInventory = snapshot =>
        {
            var device = snapshot.Devices[0];
            var volume = device.Volumes[0];
            if (change == "identity") device = device with { Identity = new("different", DeviceIdentityConfidence.Persistent) };
            if (change == "volume") volume = volume with { Identity = new("different", DeviceIdentityConfidence.Persistent) };
            if (change == "root") volume = volume with { Root = tree.Outside };
            if (change == "state") volume = volume with { State = DeviceState.Unavailable };
            return snapshot with { Devices = [device with { Volumes = [volume] }] };
        };
        Assert.Equal(GameCopyErrorCode.DestinationChanged,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["game.iso"], null, default))).Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
        Assert.Empty(Directory.GetFileSystemEntries(tree.Outside));
    }

    [LinuxCopyFact]
    public async Task VolumeChangeImmediatelyBeforeTruncationLeavesOriginalUntouched()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        var path = Path.Combine(tree.Root, "ISO", "game.iso");
        File.WriteAllText(path, "keep");
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (stage, _) =>
        { if (stage == "truncate") tree.Generation = "replaced"; } });
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        var consent = (await destination.InspectAsync(["game.iso"], default)).Evidence;
        Assert.Equal(GameCopyErrorCode.DestinationChanged,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["game.iso"], consent, default))).Error.Code);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    [LinuxCopyTheory]
    [InlineData(0L, GameCopyErrorCode.InsufficientSpace)]
    [InlineData(null, GameCopyErrorCode.UnknownFreeSpace)]
    public async Task CapacityPreflightCanRejectWithoutAnyMutation(long? free, GameCopyErrorCode expected)
    {
        using var tree = new CopyTestTree();
        var fileSystem = tree.CreateFileSystem(new() { AvailableBytes = () => free });
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(expected, GameCopyRules.CheckCapacity(1, await destination.GetAvailableBytesAsync(default))!.Code);
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyFact]
    public async Task NativeOpenErrorKeepsOriginalCauseWithoutCreatingAnOutput()
    {
        using var tree = new CopyTestTree();
        var fault = new IOException("Injected open error.");
        var fileSystem = tree.CreateFileSystem(new() { BeforeMutation = (stage, _) => { if (stage == "open") throw fault; } });
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        var error = await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync(["game.iso"], null, default));
        Assert.Equal(GameCopyErrorCode.WriteError, error.Error.Code);
        Assert.Same(fault, error.InnerException);
        Assert.False(File.Exists(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAndCloseFaultsReleaseHandlesAndRetainOutput(bool closeFailure)
    {
        using var tree = new CopyTestTree();
        var fault = new IOException(closeFailure ? "Injected close failure." : "Injected disk full.");
        FaultingFileStream? sink = null;
        var fileSystem = tree.CreateFileSystem(new() { WrapOutput = stream => sink = new(stream, fault, closeFailure) });
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        GameCopyException error;
        if (closeFailure)
        {
            await output.Output.WriteAsync(new byte[] { 1, 2, 3 });
            error = await Assert.ThrowsAsync<GameCopyException>(() => output.DisposeAsync().AsTask());
        }
        else
        {
            error = await Assert.ThrowsAsync<GameCopyException>(async () => await output.Output.WriteAsync(new byte[] { 1, 2, 3 }));
            await output.DisposeAsync();
        }
        Assert.Equal(closeFailure ? GameCopyErrorCode.CloseError : GameCopyErrorCode.WriteError, error.Error.Code);
        Assert.Same(fault, error.InnerException);
        Assert.True(sink!.Closed);
        Assert.Equal(closeFailure ? new byte[] { 1, 2, 3 } : new byte[] { 1 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task CancellationDuringPendingWriteClosesWithoutCleanup()
    {
        using var tree = new CopyTestTree();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fileSystem = tree.CreateFileSystem(new() { WrapOutput = stream => new PendingFileStream(stream, entered) });
        await using var destination = await fileSystem.OpenDestinationAsync(tree.Evidence, default);
        var output = (await destination.OpenOutputAsync(["game.iso"], null, default)).File!;
        using var cancellation = new CancellationTokenSource();
        var writing = output.Output.WriteAsync(new byte[] { 1, 2 }, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        await output.DisposeAsync();
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task InvalidLongNameFailsAndValidLinuxColonNameIsPreserved()
    {
        using var tree = new CopyTestTree();
        await using var destination = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        Assert.Equal(GameCopyErrorCode.InvalidName,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await destination.OpenOutputAsync([new string('x', 300)], null, default))).Error.Code);
        await using var output = (await destination.OpenOutputAsync(["game:disc.iso"], null, default)).File!;
        await output.Output.WriteAsync(new byte[] { 1 });
        Assert.True(File.Exists(Path.Combine(tree.Root, "ISO", "game:disc.iso")));
    }

    private class ForwardFileStream(Stream inner) : Stream
    {
        protected Stream Inner => inner;
        public bool Closed { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken token = default) => inner.WriteAsync(b, token);
        public override long Seek(long o, SeekOrigin origin) => inner.Seek(o, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override async ValueTask DisposeAsync() { Closed = true; await inner.DisposeAsync(); }
        protected override void Dispose(bool disposing) { if (disposing) { Closed = true; inner.Dispose(); } base.Dispose(disposing); }
    }
    private sealed class FaultingFileStream(Stream inner, IOException fault, bool closeFailure) : ForwardFileStream(inner)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            await Inner.WriteAsync(closeFailure ? bytes : bytes[..1], token);
            if (!closeFailure) throw fault;
        }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); if (closeFailure) throw fault; }
    }
    private sealed class PendingFileStream(Stream inner, TaskCompletionSource entered) : ForwardFileStream(inner)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            await Inner.WriteAsync(bytes[..1], token);
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }
}
