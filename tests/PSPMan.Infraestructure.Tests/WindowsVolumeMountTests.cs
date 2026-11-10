using System.Buffers.Binary;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsVolumeMountTests
{
    private const string GuidPath = @"\\?\Volume{00000000-0000-0000-0000-000000000001}\";
    private static readonly WindowsVolumeMountTarget Target = new("reader", "disk", GuidPath, new(7, 1), 42);

    [Theory]
    [InlineData("E:\\")]
    [InlineData("C:\\Mounted\\Games\\")]
    public async Task ReusesExistingRootsWithoutAssignment(string root)
    {
        var fake = new Api { Snapshot = Snapshot(root) };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Equal(root, Assert.Single(result.Volume!.MountPoints));
        Assert.Empty(fake.Assignments);
        Assert.Equal(0, fake.LetterQueries);
    }

    [Fact]
    public async Task AssignsOnlyFreeLetterAndReturnsObservedRootRatherThanRequestedRoot()
    {
        var fake = new Api { Occupied = 0b11111 }; // A-E, including unrelated/internal/network mappings.
        fake.Assign = (_, root) =>
        {
            Assert.Equal("F:\\", root);
            fake.Snapshot = Snapshot("G:\\"); // Another actor changed the actual root.
            return null;
        };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Equal("G:\\", Assert.Single(result.Volume!.MountPoints));
        Assert.Single(fake.Assignments);
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("no-media")]
    [InlineData("offline")]
    [InlineData("device-error")]
    [InlineData("volume-error")]
    [InlineData("ambiguous")]
    [InlineData("replaced")]
    [InlineData("missing")]
    public async Task RejectsUnsafeUnavailableOrReplacedTargetsWithoutNativeMutation(string scenario)
    {
        var snapshot = Snapshot();
        var device = snapshot.Devices[0];
        var volume = snapshot.Volumes[0];
        snapshot = scenario switch
        {
            "internal" => snapshot with { Devices = [device with { Bus = WindowsStorageBus.Sata, IsRemovable = false }] },
            "no-media" => snapshot with { Volumes = [volume with { State = WindowsVolumeState.NoMedia }] },
            "offline" => snapshot with { Volumes = [volume with { State = WindowsVolumeState.Unavailable }] },
            "device-error" => snapshot with { Devices = [device with { Error = new("NativeFailure", "Read") }] },
            "volume-error" => snapshot with { Volumes = [volume with { Error = new("AccessDenied", "Read", 5) }] },
            "ambiguous" => snapshot with { Devices = [device, device with { InstanceId = "other" }] },
            "replaced" => snapshot with { Volumes = [volume with { FileSystemSerialNumber = 43 }] },
            _ => new([], [], [])
        };
        var fake = new Api { Snapshot = snapshot };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Empty(fake.Assignments);
    }

    [Fact]
    public async Task RevalidatesAfterLetterQueryBeforeAssignment()
    {
        var fake = new Api();
        fake.OnLetters = () => fake.Snapshot = new([], [], []);
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Empty(fake.Assignments);
        Assert.Empty(result.Snapshot.Devices);
    }

    [Fact]
    public async Task LetterConflictRefreshesAndRetriesWithAnotherFreeLetter()
    {
        var fake = new Api();
        fake.Assign = (_, root) =>
        {
            if (fake.Assignments.Count == 1)
            {
                fake.Occupied |= 1u << (root[0] - 'A');
                return new("DriveLetterConflict", "SetVolumeMountPoint", 183);
            }
            fake.Snapshot = Snapshot(root);
            return null;
        };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "D:\\", "E:\\" }, fake.Assignments);
        Assert.True(fake.Reads >= 5);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1314)]
    [InlineData(21)]
    public async Task PreservesNativeErrorsAndObservedStateWithoutPrivilegeRetry(int nativeCode)
    {
        var error = new WindowsDeviceError("NativeFailure", "SetVolumeMountPoint", nativeCode, "Native message");
        var fake = new Api { Assign = (_, _) => error };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Same(error, result.Error);
        Assert.Equal(nativeCode is 5 or 1314, result.RequiresAuthorization);
        Assert.Empty(result.Volume!.MountPoints);
        Assert.Single(fake.Assignments);
        Assert.True(fake.Reads >= 3);
    }

    [Fact]
    public async Task NativeSuccessWithoutObservedMountIsNotSuccess()
    {
        var fake = new Api();
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("MountNotObserved", result.Error?.Code);
        Assert.Empty(result.Volume!.MountPoints);
    }

    [Fact]
    public async Task DisconnectDuringAssignmentDoesNotFabricateMountedVolume()
    {
        var fake = new Api();
        fake.Assign = (_, _) => { fake.Snapshot = new([], [], []); return null; };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Null(result.Volume);
    }

    [Fact]
    public async Task ExhaustedLettersAndLetterQueryFailuresNeverAssign()
    {
        var fake = new Api { Occupied = uint.MaxValue };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.Equal("NoDriveLetterAvailable", result.Error?.Code);
        Assert.Empty(fake.Assignments);
        fake.LetterError = new("AccessDenied", "GetLogicalDrives", 5);
        result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.Same(fake.LetterError, result.Error);
        Assert.Empty(fake.Assignments);
    }

    [Fact]
    public async Task ConflictsAreBoundedAndNeverRepeatedOnSameLetter()
    {
        var fake = new Api { Assign = (_, _) => new("DriveLetterConflict", "SetVolumeMountPoint", 183) };
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("DriveLetterConflict", result.Error?.Code);
        Assert.InRange(fake.Assignments.Count, 1, 3);
        Assert.Equal(fake.Assignments.Count, fake.Assignments.Distinct().Count());
    }

    [Fact]
    public async Task MultipleVolumesAreMountedIndependentlyWithoutChangingExistingRoots()
    {
        var fake = new Api();
        var second = fake.Snapshot.Volumes[0] with
        {
            VolumeGuidPath = @"\\?\Volume{00000000-0000-0000-0000-000000000002}\", MountPoints = ["Q:\\"]
        };
        fake.Snapshot = fake.Snapshot with { Volumes = [fake.Snapshot.Volumes[0], second] };
        fake.Assign = (_, root) =>
        {
            fake.Snapshot = fake.Snapshot with { Volumes = [fake.Snapshot.Volumes[0] with { MountPoints = [root] }, second] };
            return null;
        };
        var service = new WindowsVolumeMountService(fake, fake);
        Assert.True((await service.MountAsync(Target)).Succeeded);
        var result = await service.MountAsync(Target with { VolumeGuidPath = second.VolumeGuidPath! });
        Assert.True(result.Succeeded);
        Assert.Equal("Q:\\", Assert.Single(result.Volume!.MountPoints));
        Assert.Single(fake.Assignments);
    }

    [Fact]
    public async Task CancellationBeforeMutationAndInvalidTargetsDoNotAssign()
    {
        var fake = new Api();
        using var cancellation = new CancellationTokenSource();
        fake.OnLetters = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsVolumeMountService(fake, fake).MountAsync(Target, cancellation.Token).AsTask());
        Assert.Empty(fake.Assignments);
        fake.OnLetters = null;
        var result = await new WindowsVolumeMountService(fake, fake).MountAsync(Target with { VolumeGuidPath = "C:\\" });
        Assert.Equal("InvalidMountTarget", result.Error?.Code);
        Assert.Empty(fake.Assignments);
    }

    [Fact]
    public async Task NativeMountBoundaryIsGuardedOutsideWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        var api = new WindowsVolumeMountApi();
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => api.GetOccupiedDriveLettersAsync().AsTask());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => api.AssignRootAsync(Target, "D:\\").AsTask());
    }

    [Theory]
    [InlineData(0UL, true)]
    [InlineData(1UL, false)]
    [InlineData(0x4000000000000000UL, false)]
    [InlineData(0x8000000000000000UL, false)]
    [InlineData(0x1000000000000000UL, true)] // Read-only data is still a valid source.
    public void GptPolicyPreservesHiddenRequiredAndNoDriveLetterAttributes(ulong attributes, bool eligible)
    {
        var bytes = new byte[144];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        new Guid("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7").TryWriteBytes(bytes.AsSpan(32));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64), attributes);
        Assert.Equal(eligible, WindowsVolumeMountApi.IsAssignablePartition(bytes));
        new Guid("c12a7328-f81f-11d2-ba4b-00a0c93ec93b").TryWriteBytes(bytes.AsSpan(32));
        Assert.False(WindowsVolumeMountApi.IsAssignablePartition(bytes));
    }

    [Theory]
    [InlineData(0x0b, false, true)]
    [InlineData(0x07, false, true)]
    [InlineData(0x17, false, false)]
    [InlineData(0x27, false, false)]
    [InlineData(0x0b, true, false)]
    public void MbrPolicyRejectsHiddenRecoveryAndServicePartitions(byte type, bool service, bool eligible)
    {
        var bytes = new byte[144];
        bytes[29] = service ? (byte)1 : (byte)0;
        bytes[32] = type;
        bytes[34] = 1;
        Assert.Equal(eligible, WindowsVolumeMountApi.IsAssignablePartition(bytes));
        Assert.False(WindowsVolumeMountApi.IsAssignablePartition(bytes.AsSpan(0, 35)));
    }

    private static WindowsStorageSnapshot Snapshot(string? root = null) => new(
        [new("reader", null, "disk", "Reader", new(7, 1), WindowsStorageBus.Usb, false, true, true)],
        [new("volume", new(7, 1)) { VolumeGuidPath = GuidPath, FileSystemSerialNumber = 42,
            State = WindowsVolumeState.Available, MountPoints = root is null ? [] : [root] }], []);

    private sealed class Api : IWindowsDeviceApi, IWindowsVolumeMountApi
    {
        public WindowsStorageSnapshot Snapshot { get; set; } = WindowsVolumeMountTests.Snapshot();
        public uint Occupied { get; set; } = 7;
        public WindowsDeviceError? LetterError { get; set; }
        public Action? OnLetters { get; set; }
        public Func<WindowsVolumeMountTarget, string, WindowsDeviceError?>? Assign { get; set; }
        public List<string> Assignments { get; } = [];
        public int Reads { get; private set; }
        public int LetterQueries { get; private set; }
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return ValueTask.FromResult(Snapshot);
        }
        public ValueTask<WindowsDriveLetterObservation> GetOccupiedDriveLettersAsync(CancellationToken cancellationToken = default)
        {
            LetterQueries++;
            OnLetters?.Invoke();
            return ValueTask.FromResult(new WindowsDriveLetterObservation(Occupied, LetterError));
        }
        public ValueTask<WindowsDeviceError?> AssignRootAsync(WindowsVolumeMountTarget target, string root,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True((Occupied & (1u << (root[0] - 'A'))) == 0);
            Assignments.Add(root);
            return ValueTask.FromResult(Assign?.Invoke(target, root));
        }
    }
}
