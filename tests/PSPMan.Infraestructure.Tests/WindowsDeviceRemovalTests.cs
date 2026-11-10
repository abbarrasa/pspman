using System.Collections.Immutable;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceRemovalTests
{
    private static readonly WindowsDeviceRemovalTarget Target = new("disk-id", "disk-interface", new(7, 1));

    [Fact]
    public async Task PreparesEveryVolumeReleasesOwnHandlesAndEjectsOnlySelectedPhysicalNode()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(new[] { "volume-1", "volume-2" }, fixture.Native.Locked);
        Assert.Equal("disk-id", fixture.Native.LocatedId);
        Assert.Equal(42u, Assert.Single(fixture.Native.Ejected));
        Assert.Equal(0, fixture.Native.Active);
        Assert.Equal("sibling", Assert.Single(result.Snapshot.Devices).InstanceId);
        Assert.Equal("Z:\\", Assert.Single(Assert.Single(result.Snapshot.Volumes).MountPoints));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("lock")]
    [InlineData("number")]
    public async Task PartialPreparationFailureReleasesAllHandlesAndDoesNotEject(string failure)
    {
        var fixture = new Fixture();
        fixture.Native.FailAt = 2;
        fixture.Native.FailureStage = failure;
        fixture.Native.OnLock = () => fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        {
            Volumes = fixture.Inventory.Snapshot.Volumes.SetItem(0,
                fixture.Inventory.Snapshot.Volumes[0] with { MountPoints = [] })
        };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Empty(fixture.Native.Ejected);
        Assert.Equal(0, fixture.Native.Active);
        Assert.Empty(result.Snapshot.Volumes[0].MountPoints);
        Assert.Equal("F:\\", Assert.Single(result.Snapshot.Volumes[1].MountPoints));
    }

    [Fact]
    public async Task PreservesConfigurationStatusVetoTypeAndNameEvenIfInventoryDisappears()
    {
        var fixture = new Fixture();
        fixture.Native.EjectFailure = new(new("RemovalVetoed", "CM_Request_Device_Eject", 0x17))
        { ConfigurationStatus = 0x17, VetoType = 5, VetoName = "busy-device" };
        fixture.Native.BeforeEjectReply = () => fixture.Inventory.Snapshot = new([], [], []);
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Same(fixture.Native.EjectFailure, result.Failure);
        Assert.Equal(0x17u, result.Failure!.ConfigurationStatus);
        Assert.Equal(5u, result.Failure.VetoType);
        Assert.Equal("busy-device", result.Failure.VetoName);
        Assert.Equal(0, fixture.Native.Active);
        Assert.Empty(result.Snapshot.Devices);
    }

    [Fact]
    public async Task LocateFailureIsPreservedAfterReleasingPreparationHandles()
    {
        var fixture = new Fixture();
        fixture.Native.LocateFailure = new(new("ConfigurationManagerFailure", "CM_Locate_DevNode", 5))
        { ConfigurationStatus = 5 };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Same(fixture.Native.LocateFailure, result.Failure);
        Assert.False(result.Failure!.RequiresAuthorization);
        Assert.Empty(fixture.Native.Ejected);
        Assert.Equal(0, fixture.Native.Active);
    }

    [Fact]
    public async Task UnavailableVolumeErrorDoesNotReachNativeMutation()
    {
        var fixture = new Fixture();
        var error = new WindowsDeviceError("AccessDenied", "ReadVolume", 5, "Native error");
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = fixture.Inventory.Snapshot.Volumes.SetItem(1, fixture.Inventory.Snapshot.Volumes[1] with
        { State = WindowsVolumeState.Unavailable, MountPoints = [], Error = error }) };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Same(error, result.Failure!.Error);
        Assert.True(result.Failure.RequiresAuthorization);
        Assert.Equal(fixture.Inventory.Snapshot.Volumes[1].VolumeGuidPath, result.Failure.VolumeGuidPath);
        Assert.Empty(fixture.Native.Opened);
    }

    [Fact]
    public async Task UnexpectedPreparationFailureStillClosesAllAcquiredHandles()
    {
        var fixture = new Fixture();
        fixture.Native.OnLock = () => throw new IOException("Injected control failure");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.RemoveAsync(Target).AsTask());
        Assert.Equal(0, fixture.Native.Active);
        Assert.Empty(fixture.Native.Ejected);
    }

    [Fact]
    public void BusyVolumeDoesNotAutomaticallyRequestElevation()
    {
        var failure = new WindowsDeviceRemovalFailure(new("VolumeBusy", "FSCTL_LOCK_VOLUME", 5));
        Assert.False(failure.RequiresAuthorization);
    }

    [Fact]
    public async Task SuccessfulNativeReplyDoesNotFabricateConfirmedRemoval()
    {
        var fixture = new Fixture();
        fixture.Native.OnEject = () => { };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("RemovalIncomplete", result.Failure?.Error.Code);
        Assert.Equal(2, result.Snapshot.Devices.Length);
    }

    [Fact]
    public async Task MissingDeviceWithRemainingVolumeOrFailedInventoryDoesNotConfirmRemoval()
    {
        var fixture = new Fixture();
        fixture.Native.OnEject = () => fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Devices = [fixture.Inventory.Snapshot.Devices[1]] };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("RemovalIncomplete", result.Failure?.Error.Code);

        fixture = new Fixture();
        fixture.Native.OnEject = () => fixture.Inventory.Snapshot = new([], [], [new("NativeFailure", "Enumerate")]);
        result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("NativeFailure", result.Failure?.Error.Code);
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("optical")]
    [InlineData("ambiguous")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("error")]
    [InlineData("incomplete-inventory")]
    public async Task RejectsUnsafeOrChangedDeviceWithoutOpeningAnyVolume(string scenario)
    {
        var fixture = new Fixture();
        var snapshot = fixture.Inventory.Snapshot;
        var device = snapshot.Devices[0];
        fixture.Inventory.Snapshot = scenario switch
        {
            "internal" => snapshot with { Devices = [device with { Bus = WindowsStorageBus.Sata, IsRemovable = false }] },
            "optical" => snapshot with { Devices = [device with { IsOptical = true }] },
            "ambiguous" => snapshot with { Devices = [device, device with { InstanceId = "other" }] },
            "changed" => snapshot with { Devices = [device with { StorageNumber = new(7, 9) }] },
            "error" => snapshot with { Devices = [device with { Error = new("AccessDenied", "Read", 5) }] },
            "incomplete-inventory" => snapshot with { Errors = [new("NativeFailure", "Enumerate")] },
            _ => new([], [], [])
        };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Empty(fixture.Native.Opened);
        Assert.Empty(fixture.Native.Ejected);
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("")]
    public async Task InvalidVolumeOrDeviceIdentifiersCannotReachNativeMutation(string path)
    {
        var fixture = new Fixture();
        var volume = fixture.Inventory.Snapshot.Volumes[0];
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = [volume with { VolumeGuidPath = path }] };
        Assert.False((await fixture.Service.RemoveAsync(Target)).Succeeded);
        Assert.Empty(fixture.Native.Opened);
        Assert.False((await fixture.Service.RemoveAsync(Target with { InstanceId = "" })).Succeeded);
        Assert.Null(fixture.Native.LocatedId);
    }

    [Fact]
    public async Task VolumeTopologyChangeAfterPreparationRejectsRequestWithoutEject()
    {
        var fixture = new Fixture();
        fixture.Native.OnLock = () => fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        {
            Volumes = fixture.Inventory.Snapshot.Volumes.SetItem(0,
                fixture.Inventory.Snapshot.Volumes[0] with { FileSystemSerialNumber = 99 })
        };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.False(result.Succeeded);
        Assert.Equal("TargetChanged", result.Failure?.Error.Code);
        Assert.Equal(0, fixture.Native.Active);
        Assert.Empty(fixture.Native.Ejected);
    }

    [Fact]
    public async Task CancellationDuringPreparationReleasesAllHandlesAndDoesNotEject()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Native.OnLock = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.RemoveAsync(Target, cancellation.Token).AsTask());
        Assert.Empty(fixture.Native.Ejected);
        Assert.Equal(0, fixture.Native.Active);
    }

    [Fact]
    public async Task EmptyReaderCanBeRemovedWithoutInventingVolumeRoots()
    {
        var fixture = new Fixture();
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with { Volumes = [fixture.Inventory.Snapshot.Volumes[2]] };
        var result = await fixture.Service.RemoveAsync(Target);
        Assert.True(result.Succeeded);
        Assert.Empty(fixture.Native.Opened);
        Assert.Single(fixture.Native.Ejected);
    }

    [Theory]
    [InlineData(null, 5, null, true)]
    [InlineData(null, 1314, null, true)]
    [InlineData(0x33u, 0x33, null, true)]
    [InlineData(0x17u, 0x17, 12u, true)]
    [InlineData(5u, 5, null, false)] // CR_INVALID_DEVNODE is NOT Win32 ERROR_ACCESS_DENIED.
    [InlineData(0x17u, 0x17, 5u, false)]
    public void AuthorizationNeedKeepsWin32AndConfigurationStatusNamespacesSeparate(uint? status, int code, uint? veto, bool expected)
    {
        var failure = new WindowsDeviceRemovalFailure(new("NativeFailure", "Remove", code))
        { ConfigurationStatus = status, VetoType = veto };
        Assert.Equal(expected, failure.RequiresAuthorization);
    }

    [Fact]
    public async Task ExplicitRetryCanSucceedAfterVetoWithoutAnAutomaticRequestLoop()
    {
        var fixture = new Fixture();
        fixture.Native.EjectFailure = new(new("RemovalVetoed", "CM_Request_Device_Eject", 0x17)) { ConfigurationStatus = 0x17 };
        Assert.False((await fixture.Service.RemoveAsync(Target)).Succeeded);
        Assert.Single(fixture.Native.Ejected);
        fixture.Native.EjectFailure = null;
        Assert.True((await fixture.Service.RemoveAsync(Target)).Succeeded);
        Assert.Equal(2, fixture.Native.Ejected.Count);
    }

    [Fact]
    public async Task ProductionNativeBoundaryIsGuardedOutsideWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new WindowsDeviceRemovalApi().RequestRemovalAsync(Target).AsTask());
    }

    private sealed class Fixture
    {
        public Inventory Inventory { get; } = new();
        public Native Native { get; }
        public WindowsDeviceRemovalService Service { get; }
        public Fixture()
        {
            Native = new Native();
            Inventory.AssertNoHandles = () => Assert.Equal(0, Native.Active);
            Native.OnEject = () => Inventory.Snapshot = Inventory.Snapshot with
            {
                Devices = Inventory.Snapshot.Devices.Where(device => device.InstanceId != Target.InstanceId).ToImmutableArray(),
                Volumes = Inventory.Snapshot.Volumes.Where(volume => volume.StorageNumber != Target.StorageNumber).ToImmutableArray()
            };
            Service = new(Inventory, new WindowsDeviceRemovalApi(Inventory, Native));
        }
    }

    private sealed class Inventory : IWindowsDeviceApi
    {
        public WindowsStorageSnapshot Snapshot { get; set; } = new(
            [new("disk-id", "shared-hub", "disk-interface", "Reader", new(7, 1), WindowsStorageBus.Usb, false, true, true),
             new("sibling", "shared-hub", "sibling-interface", "Other reader", new(7, 2), WindowsStorageBus.Usb, false, true, true)],
            [Volume(1, 1, "E:\\"), Volume(2, 1, "F:\\"), Volume(3, 2, "Z:\\")], []);
        public Action? AssertNoHandles { get; set; }
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssertNoHandles?.Invoke();
            return ValueTask.FromResult(Snapshot);
        }
        private static WindowsVolumeReference Volume(int id, uint disk, string root) => new($"volume-{id}", new(7, disk))
        {
            VolumeGuidPath = $"\\\\?\\Volume{{00000000-0000-0000-0000-{id:D12}}}\\",
            State = WindowsVolumeState.Available, FileSystemSerialNumber = (uint)id, MountPoints = [root]
        };
    }

    private sealed class Native : IWindowsRemovalNativeApi
    {
        public int Active { get; private set; }
        public int FailAt { get; set; }
        public string? FailureStage { get; set; }
        public List<string> Opened { get; } = [];
        public List<string> Locked { get; } = [];
        public List<uint> Ejected { get; } = [];
        public string? LocatedId { get; private set; }
        public WindowsDeviceRemovalFailure? EjectFailure { get; set; }
        public WindowsDeviceRemovalFailure? LocateFailure { get; set; }
        public Action? OnLock { get; set; }
        public Action? OnEject { get; set; }
        public Action? BeforeEjectReply { get; set; }
        public IWindowsRemovalVolume? OpenVolume(WindowsVolumeReference volume, out WindowsDeviceError? error)
        {
            Opened.Add(volume.InterfacePath);
            error = Opened.Count == FailAt && FailureStage == "open" ? new("AccessDenied", "OpenVolume", 5) : null;
            if (error is not null) return null;
            Active++;
            return new Handle(this, volume, Opened.Count);
        }
        public WindowsDeviceRemovalFailure? LocateDevice(string instanceId, out uint node)
        {
            Assert.Equal(0, Active);
            LocatedId = instanceId;
            node = 42;
            return LocateFailure;
        }
        public WindowsDeviceRemovalFailure? RequestEject(uint node)
        {
            Assert.Equal(0, Active);
            Ejected.Add(node);
            BeforeEjectReply?.Invoke();
            if (EjectFailure is null) OnEject?.Invoke();
            return EjectFailure;
        }
        private sealed class Handle(Native owner, WindowsVolumeReference volume, int index) : IWindowsRemovalVolume
        {
            private bool _disposed;
            public WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error)
            {
                error = null;
                return index == owner.FailAt && owner.FailureStage == "number" ? new(7, 99) : volume.StorageNumber;
            }
            public WindowsDeviceError? Lock()
            {
                if (index == owner.FailAt && owner.FailureStage == "lock") return new("VolumeBusy", "FSCTL_LOCK_VOLUME", 32);
                owner.Locked.Add(volume.InterfacePath);
                owner.OnLock?.Invoke();
                return null;
            }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.Active--;
            }
        }
    }
}
