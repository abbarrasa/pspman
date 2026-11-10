using System.Collections.Immutable;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsOpticalMediaEjectorTests
{
    private static readonly WindowsOpticalMediaTarget Target = new("optical", "cdrom-interface", new(2, 0));

    [Fact]
    public async Task EjectsMediaWhileHoldingAllVolumeLocksAndRetainsDriveAndSibling()
    {
        var fixture = new Fixture();
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Single(fixture.Native.Ejections);
        Assert.Equal(2, result.Snapshot.Devices.Length);
        Assert.True(result.Snapshot.Devices[0].IsOptical);
        Assert.Equal("Z:\\", Assert.Single(Assert.Single(result.Snapshot.Volumes).MountPoints));
        Assert.Equal(0, fixture.Native.Active);
        Assert.Equal(0, fixture.Native.Locks);
        Assert.Contains("cdrom-interface", fixture.Native.Paths);
    }

    [Fact]
    public async Task EmptyReaderWithMissingStorageNumberIsConfirmedWithoutInventingIdentityOrEjecting()
    {
        var fixture = new Fixture();
        fixture.Empty();
        var device = fixture.Inventory.Snapshot.Devices[0];
        var noMedia = new WindowsDeviceError("NoMedia", "ReadStorage", 21);
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Devices = [device with { StorageNumber = null, Error = noMedia }, fixture.Inventory.Snapshot.Devices[1]], Errors = [noMedia] };
        var result = await fixture.Ejector.EjectAsync(Target with { StorageNumber = null });
        Assert.True(result.Succeeded);
        Assert.Null(result.Snapshot.Devices[0].StorageNumber);
        Assert.Empty(fixture.Native.Ejections);
        Assert.Equal(0, fixture.Native.Active);
    }

    [Fact]
    public async Task EmptyReaderNeverBorrowsAnotherDrivesVolumesUsingItsOldStorageNumber()
    {
        var fixture = new Fixture();
        fixture.Native.MediaPresent = false;
        var snapshot = fixture.Inventory.Snapshot;
        var empty = snapshot.Devices[0] with
        { StorageNumber = null, Error = new("NoMedia", "ReadStorage", 21) };
        var other = snapshot.Devices[0] with { InstanceId = "other-optical", InterfacePath = "other-interface" };
        // The old (2,0) pair now belongs to another optical device, not the empty selected reader.
        fixture.Inventory.Snapshot = snapshot with { Devices = [empty, snapshot.Devices[1], other] };
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Snapshot.Devices.Length);
        Assert.Equal("E:\\", Assert.Single(result.Snapshot.Volumes[0].MountPoints));
        Assert.All(fixture.Native.Paths, path => Assert.Equal("cdrom-interface", path));
        Assert.Empty(fixture.Native.Ejections);
    }

    [Fact]
    public async Task MissingFilesystemVolumesDoNotProveThatDriveIsEmpty()
    {
        var fixture = new Fixture();
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with { Volumes = [fixture.Inventory.Snapshot.Volumes[1]] };
        // Even without a volume GUID, present media must pass the ordinary drive-handle lock check.
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Single(fixture.Native.Ejections);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("number")]
    [InlineData("lock")]
    [InlineData("verify")]
    [InlineData("eject")]
    public async Task PreservesNativeFailureAndActualStateWithoutRemovingDrive(string stage)
    {
        var fixture = new Fixture();
        fixture.Native.FailureStage = stage;
        if (stage == "eject") fixture.Native.OnEject = () => { };
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Failure!.Error.NativeCode);
        Assert.Equal(2, result.Snapshot.Devices.Length);
        Assert.Equal("E:\\", Assert.Single(result.Snapshot.Volumes[0].MountPoints));
        Assert.Equal(0, fixture.Native.Active);
        Assert.Equal(0, fixture.Native.Locks);
        Assert.Equal(stage == "eject" ? 1 : 0, fixture.Native.Ejections.Count);
    }

    [Theory]
    [InlineData("usb")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("ambiguous")]
    [InlineData("device-error")]
    [InlineData("inventory-error")]
    public async Task RejectsInvalidOrNonOpticalTargetsBeforeOpeningNativeHandle(string scenario)
    {
        var fixture = new Fixture();
        var snapshot = fixture.Inventory.Snapshot;
        var device = snapshot.Devices[0];
        fixture.Inventory.Snapshot = scenario switch
        {
            "usb" => snapshot with { Devices = [device with { IsOptical = false }] },
            "missing" => new([], [], []),
            "changed" => snapshot with { Devices = [device with { StorageNumber = new(2, 9) }] },
            "ambiguous" => snapshot with { Devices = [device, device] },
            "device-error" => snapshot with { Devices = [device with { Error = new("AccessDenied", "Read", 5) }] },
            _ => snapshot with { Errors = [new("NativeFailure", "Enumerate")] }
        };
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Empty(fixture.Native.Paths);
        Assert.Empty(fixture.Native.Ejections);
    }

    [Fact]
    public async Task SuccessReplyWithMediaRemainingIsNotSuccessfulEjection()
    {
        var fixture = new Fixture();
        fixture.Native.OnEject = () => { };
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("EjectionIncomplete", result.Failure?.Error.Code);
    }

    [Fact]
    public async Task DriveDisappearanceIsNotSuccessfulMediaEjection()
    {
        var fixture = new Fixture();
        fixture.Native.OnEject = () => fixture.Inventory.Snapshot = new([], [], []);
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("TargetUnavailable", result.Failure?.Error.Code);
    }

    [Fact]
    public async Task NativeMediaPresenceCheckPreventsFalseSuccessWithEmptyVolumeInventory()
    {
        var fixture = new Fixture();
        fixture.Native.OnEject = () => fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = [fixture.Inventory.Snapshot.Volumes[1]] };
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("EjectionIncomplete", result.Failure?.Error.Code);
        Assert.Equal(0, fixture.Native.Active);
    }

    [Fact]
    public async Task FailureRemainsFailureWhenMediaDisappearsConcurrently()
    {
        var fixture = new Fixture();
        fixture.Native.FailureStage = "eject";
        fixture.Native.OnEject = fixture.Empty;
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Failure?.Error.NativeCode);
        Assert.Single(result.Snapshot.Volumes);
        Assert.True(result.Snapshot.Devices[0].IsOptical);
    }

    [Fact]
    public async Task ChangedMediaCountBeforeEjectionStopsRequest()
    {
        var fixture = new Fixture();
        fixture.Native.ChangeMediaOnSecondCheck = true;
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("MediaChanged", result.Failure?.Error.Code);
        Assert.Empty(fixture.Native.Ejections);
        Assert.Equal(0, fixture.Native.Active);
    }

    [Fact]
    public async Task CancellationWhileLockedReleasesResourcesWithoutEjecting()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Native.OnLock = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Ejector.EjectAsync(Target, cancellation.Token).AsTask());
        Assert.Equal(0, fixture.Native.Active);
        Assert.Equal(0, fixture.Native.Locks);
        Assert.Empty(fixture.Native.Ejections);
    }

    [Fact]
    public async Task PreparesMultipleVolumesAndReleasesEarlierLockWhenLaterVolumeIsBusy()
    {
        var fixture = new Fixture();
        var volume = fixture.Inventory.Snapshot.Volumes[0];
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = fixture.Inventory.Snapshot.Volumes.Add(volume with
        { VolumeGuidPath = @"\\?\Volume{00000000-0000-0000-0000-000000000002}\" }) };
        fixture.Native.FailSecondLock = true;
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Native.Ejections);
        Assert.Equal(0, fixture.Native.Active);
        Assert.Equal(0, fixture.Native.Locks);
    }

    [Fact]
    public async Task MediaEjectionDoesNotUsePhysicalRemovalBranch()
    {
        var fixture = new Fixture();
        var target = new WindowsDeviceRemovalTarget(Target.InstanceId, Target.InterfacePath, Target.StorageNumber!.Value);
        var result = await new WindowsDeviceRemovalService(fixture.Inventory, new ForbiddenRemoval()).RemoveAsync(target);
        Assert.False(result.Succeeded);
        Assert.Equal("OpticalEjectRequired", result.Failure?.Error.Code);
        Assert.True((await fixture.Ejector.EjectAsync(Target)).Succeeded);
    }

    [Fact]
    public async Task AllOpticalFilesystemLocksRemainHeldUntilMediaIsEjected()
    {
        var fixture = new Fixture();
        fixture.Native.RequireLocks = 2;
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = fixture.Inventory.Snapshot.Volumes.Add(fixture.Inventory.Snapshot.Volumes[0] with
        { VolumeGuidPath = @"\\?\Volume{00000000-0000-0000-0000-000000000002}\" }) };
        Assert.True((await fixture.Ejector.EjectAsync(Target)).Succeeded);
        Assert.Single(fixture.Native.Ejections);
        Assert.Equal(0, fixture.Native.Locks);
        Assert.Equal(0, fixture.Native.Active);
    }

    [Fact]
    public async Task NativeFailureCanBeExplicitlyRetriedWithoutAutomaticLoop()
    {
        var fixture = new Fixture();
        fixture.Native.FailureStage = "eject";
        fixture.Native.OnEject = () => { };
        Assert.False((await fixture.Ejector.EjectAsync(Target)).Succeeded);
        Assert.Single(fixture.Native.Ejections);
        fixture.Native.FailureStage = null;
        fixture.Native.OnEject = fixture.Empty;
        Assert.True((await fixture.Ejector.EjectAsync(Target)).Succeeded);
        Assert.Equal(2, fixture.Native.Ejections.Count);
    }

    [Fact]
    public async Task UnexpectedControlFailureStillReleasesLocksAndHandles()
    {
        var fixture = new Fixture();
        fixture.Native.OnLock = () => throw new IOException("Injected control failure");
        await Assert.ThrowsAsync<IOException>(() => fixture.Ejector.EjectAsync(Target).AsTask());
        Assert.Equal(0, fixture.Native.Locks);
        Assert.Equal(0, fixture.Native.Active);
        Assert.Empty(fixture.Native.Ejections);
    }

    [Fact]
    public async Task FailedPostEjectionVerificationCannotFabricateSuccess()
    {
        var fixture = new Fixture();
        fixture.Native.FailureStage = "post-verify";
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Failure?.Error.NativeCode);
        Assert.Equal(0, fixture.Native.Active);
        Assert.True(result.Snapshot.Devices[0].IsOptical);
    }

    [Fact]
    public async Task RetainsObservedNoMediaVolumeWithoutPresentingOldRootAsUsable()
    {
        var fixture = new Fixture();
        fixture.Native.OnEject = () =>
        {
            fixture.Native.MediaPresent = false;
            fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
            { Volumes = fixture.Inventory.Snapshot.Volumes.SetItem(0, fixture.Inventory.Snapshot.Volumes[0] with
            { State = WindowsVolumeState.NoMedia, MountPoints = [], Error = new("NoMedia", "GetVolumeInformation", 21) }) };
        };
        var result = await fixture.Ejector.EjectAsync(Target);
        Assert.True(result.Succeeded);
        Assert.False(result.Snapshot.Volumes[0].IsMounted);
        Assert.Empty(result.Snapshot.Volumes[0].MountPoints);
        Assert.Equal("NoMedia", result.Snapshot.Volumes[0].Error?.Code);
    }

    [Fact]
    public async Task ProductionEjectorIsGuardedOutsideWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new WindowsOpticalMediaEjector().EjectAsync(Target).AsTask());
    }

    private sealed class ForbiddenRemoval : IWindowsDeviceRemovalApi
    {
        public ValueTask<WindowsDeviceRemovalFailure?> RequestRemovalAsync(WindowsDeviceRemovalTarget target,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Physical removal is forbidden for optical ejection.");
    }

    private sealed class Fixture
    {
        public Inventory Inventory { get; } = new();
        public Native Native { get; } = new();
        public WindowsOpticalMediaEjector Ejector { get; }
        public Fixture()
        {
            Inventory.BeforeRead = () => Assert.Equal(0, Native.Active);
            Native.OnEject = Empty;
            Ejector = new(Inventory, Native);
        }
        public void Empty()
        {
            Native.MediaPresent = false;
            Inventory.Snapshot = Inventory.Snapshot with
            { Volumes = Inventory.Snapshot.Volumes.Where(volume => volume.StorageNumber != new WindowsStorageNumber(2, 0)).ToImmutableArray() };
        }
    }

    private sealed class Inventory : IWindowsDeviceApi
    {
        public WindowsStorageSnapshot Snapshot { get; set; } = new(
            [new("optical", null, "cdrom-interface", "DVD drive", new(2, 0), WindowsStorageBus.Sata, true, true, true),
             new("usb", null, "usb-interface", "USB", new(7, 1), WindowsStorageBus.Usb, false, true, true)],
            [new("optical-volume", new(2, 0)) { VolumeGuidPath = @"\\?\Volume{00000000-0000-0000-0000-000000000001}\",
                State = WindowsVolumeState.Available, FileSystemSerialNumber = 42, MountPoints = ["E:\\"] },
             new("usb-volume", new(7, 1)) { State = WindowsVolumeState.Available, MountPoints = ["Z:\\"] }], []);
        public Action? BeforeRead { get; set; }
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeRead?.Invoke();
            return ValueTask.FromResult(Snapshot);
        }
    }

    private sealed class Native : IWindowsOpticalNativeApi
    {
        public int Active { get; private set; }
        public int Locks { get; private set; }
        public int RequireLocks { get; set; } = 1;
        public bool MediaPresent { get; set; } = true;
        public string? FailureStage { get; set; }
        public bool ChangeMediaOnSecondCheck { get; set; }
        public bool FailSecondLock { get; set; }
        public Action? OnEject { get; set; }
        public Action? OnLock { get; set; }
        public List<string> Paths { get; } = [];
        public List<string> Ejections { get; } = [];
        private int _checks;
        public IWindowsOpticalHandle? Open(string path, out WindowsDeviceError? error)
        {
            Paths.Add(path);
            error = FailureStage == "open" ? Failure("Open") : null;
            if (error is not null) return null;
            Active++;
            return new Handle(this, path);
        }
        private static WindowsDeviceError Failure(string operation) => new("NativeFailure", operation, 5, "Native message");
        private sealed class Handle(Native owner, string path) : IWindowsOpticalHandle
        {
            private bool _locked;
            public WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error)
            {
                error = owner.FailureStage == "number" ? Failure("ReadNumber") : null;
                return error is null ? new(2, 0) : null;
            }
            public WindowsOpticalMediaObservation CheckMedia()
            {
                owner._checks++;
                return new(owner.MediaPresent,
                    owner.ChangeMediaOnSecondCheck && owner._checks >= 2 ? 2u : 1u,
                    owner.FailureStage == "verify" || (owner.FailureStage == "post-verify" && owner._checks >= 3)
                        ? Failure("CheckMedia") : null);
            }
            public WindowsDeviceError? Lock()
            {
                if (owner.FailureStage == "lock" || (owner.FailSecondLock && owner.Locks == 1)) return Failure("Lock");
                _locked = true;
                owner.Locks++;
                owner.OnLock?.Invoke();
                return null;
            }
            public WindowsDeviceError? Eject()
            {
                Assert.Equal(owner.RequireLocks, owner.Locks);
                owner.Ejections.Add(path);
                owner.OnEject?.Invoke();
                return owner.FailureStage == "eject" ? Failure("Eject") : null;
            }
            public void Dispose()
            {
                if (_locked) owner.Locks--;
                owner.Active--;
            }
        }
    }
}
