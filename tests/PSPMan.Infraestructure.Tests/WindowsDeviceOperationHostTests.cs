using System.Collections.Immutable;
using System.ComponentModel;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceOperationHostTests
{
    private const string DeviceId = @"USBSTOR\DISK&VEN_TEST\SERIAL";
    private const string VolumeGuid = @"\\?\Volume{00000000-0000-0000-0000-000000000033}\";
    private static readonly Guid RequestId = Guid.Parse("10000000-0000-0000-0000-000000000033");

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume)]
    [InlineData(WindowsElevatedOperation.RemoveDevice)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia)]
    public async Task ExecutesOnlyTheRequestedFixedOperationAndReturnsAnObservedCorrelatedResult(WindowsElevatedOperation operation)
    {
        var fixture = new Fixture(operation);
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.True(reply.Completed);
        Assert.Null(reply.Failure);
        Assert.Equal(RequestId, reply.RequestId);
        Assert.Equal(operation, reply.Operation);
        Assert.Equal(operation == WindowsElevatedOperation.MountVolume ? 1 : 0, fixture.Native.Mounts);
        Assert.Equal(operation == WindowsElevatedOperation.RemoveDevice ? 1 : 0, fixture.Native.Removals);
        Assert.Equal(operation == WindowsElevatedOperation.EjectOpticalMedia ? 1 : 0, fixture.Native.OpticalEjections);
        Assert.Equal(0, fixture.Native.ActiveHandles);
        Assert.Contains(fixture.Inventory.Snapshot.Devices, device => device.InstanceId == "sibling");
        Assert.Equal(reply, WindowsDeviceAuthorization.DecodeReply(WindowsDeviceAuthorization.EncodeReply(reply), fixture.Request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Host.ExecuteAsync(fixture.Request));
    }

    [Theory]
    [InlineData("missing", 1)]
    [InlineData("internal", 1)]
    [InlineData("ambiguous", 1)]
    [InlineData("error", 1)]
    [InlineData("unavailable", 1)]
    [InlineData("interface", 2)]
    [InlineData("number", 2)]
    [InlineData("serial", 2)]
    [InlineData("volume", 2)]
    [InlineData("media-state", 2)]
    [InlineData("serial", 3)]
    [InlineData("interface", 3)]
    public async Task RejectsUnavailableOrReplacedSelectionsAtHostServiceAndNativeEntry(string change, int enumeration)
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Inventory.BeforeRead = count =>
        {
            if (count != enumeration) return;
            var snapshot = fixture.Inventory.Snapshot;
            var device = snapshot.Devices[0];
            var volume = snapshot.Volumes[0];
            fixture.Inventory.Snapshot = change switch
            {
                "missing" => new([], [], []),
                "internal" => snapshot with { Devices = [device with { IsRemovable = false, Bus = WindowsStorageBus.Sata }] },
                "ambiguous" => snapshot with { Devices = [device, device with { InstanceId = @"USBSTOR\OTHER\SERIAL" }] },
                "error" => snapshot with { Errors = [new("InventoryFailed", "EnumerateStorage", 5)] },
                "unavailable" => snapshot with { Volumes = [volume with { State = WindowsVolumeState.Unavailable }] },
                "interface" => snapshot with { Devices = snapshot.Devices.SetItem(0, device with { InterfacePath = "replacement-interface" }) },
                "number" => snapshot with { Devices = snapshot.Devices.SetItem(0, device with { StorageNumber = new(7, 9) }) },
                "serial" => snapshot with { Volumes = [volume with { FileSystemSerialNumber = 99 }] },
                "volume" => snapshot with { Volumes = [] },
                _ => snapshot with { Volumes = [volume with { State = WindowsVolumeState.NoMedia, MountPoints = [] }] }
            };
        };
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.NotNull(reply.Failure);
        Assert.Equal(0, fixture.Native.Mounts + fixture.Native.Removals + fixture.Native.OpticalEjections);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Fact]
    public async Task LabelAndRootChangesDoNotInventAReplacementOrPreventSafeRemoval()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Inventory.BeforeRead = count =>
        {
            if (count == 2) fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
            { Volumes = [fixture.Inventory.Snapshot.Volumes[0] with { Label = "Renamed", MountPoints = ["Y:\\"] }] };
        };
        Assert.True((await fixture.Host.ExecuteAsync(fixture.Request)).Completed);
        Assert.Equal(1, fixture.Native.Removals);
    }

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume, "serial", 2)]
    [InlineData(WindowsElevatedOperation.MountVolume, "interface", 2)]
    [InlineData(WindowsElevatedOperation.MountVolume, "serial", 3)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia, "serial", 2)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia, "number", 2)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia, "interface", 2)]
    public async Task MountAndOpticalBranchesAlsoRejectChangedSelectionBeforeMutation(WindowsElevatedOperation operation, string change, int enumeration)
    {
        var fixture = new Fixture(operation);
        fixture.Inventory.BeforeRead = count =>
        {
            if (count != enumeration) return;
            var snapshot = fixture.Inventory.Snapshot;
            fixture.Inventory.Snapshot = change switch
            {
                "serial" => snapshot with { Volumes = [snapshot.Volumes[0] with { FileSystemSerialNumber = 99 }] },
                "number" => snapshot with { Devices = snapshot.Devices.SetItem(0, snapshot.Devices[0] with { StorageNumber = new(2, 9) }) },
                _ => snapshot with { Devices = snapshot.Devices.SetItem(0, snapshot.Devices[0] with { InterfacePath = "replacement-interface" }) }
            };
        };
        Assert.False((await fixture.Host.ExecuteAsync(fixture.Request)).Completed);
        Assert.Equal(0, fixture.Native.Mounts + fixture.Native.OpticalEjections);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia)]
    public async Task MountAndOpticalAcceptanceStillRequireIndependentObservedState(WindowsElevatedOperation operation)
    {
        var fixture = new Fixture(operation);
        fixture.Native.ObserveMutation = false;
        Assert.False((await fixture.Host.ExecuteAsync(fixture.Request)).Completed);
        Assert.Equal(1, fixture.Native.Mounts + fixture.Native.OpticalEjections);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Fact]
    public async Task AnAlreadyEmptyOpticalDriveNeedsNoInventedNumberOrNativeEjection()
    {
        var fixture = new Fixture(WindowsElevatedOperation.EjectOpticalMedia);
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Devices = fixture.Inventory.Snapshot.Devices.SetItem(0, fixture.Inventory.Snapshot.Devices[0] with { StorageNumber = null }), Volumes = [] };
        fixture.Native.MediaPresent = false;
        Assert.True((await fixture.Host.ExecuteAsync(fixture.Request with { StorageNumber = null })).Completed);
        Assert.Equal(0, fixture.Native.OpticalEjections);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Fact]
    public async Task AnAlreadyMountedVolumeReusesItsObservedRootWithoutAssigningAnother()
    {
        var fixture = new Fixture(WindowsElevatedOperation.MountVolume);
        fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = [fixture.Inventory.Snapshot.Volumes[0] with { MountPoints = ["Y:\\"] }] };
        Assert.True((await fixture.Host.ExecuteAsync(fixture.Request)).Completed);
        Assert.Equal(0, fixture.Native.Mounts);
        Assert.Equal("Y:\\", Assert.Single(fixture.Inventory.Snapshot.Volumes[0].MountPoints));
    }

    [Fact]
    public async Task NativeMediaChangeAfterLocksStillRejectsRemovalAndClosesEveryHandle()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Native.OnLock = () => fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with
        { Volumes = [fixture.Inventory.Snapshot.Volumes[0] with { FileSystemSerialNumber = 99 }] };
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal("TargetChanged", reply.Failure!.Error.Code);
        Assert.Equal(0, fixture.Native.Removals);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Fact]
    public async Task PreservesNativeConfigurationVetoAndVolumeFailureDetailsWithoutRetry()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Native.RemovalFailure = new(new("RemovalVetoed", "CM_Request_Device_Eject", 0x17, "Busy device"))
        { ConfigurationStatus = 0x17, VetoType = 5, VetoName = "Device veto", VolumeGuidPath = VolumeGuid };
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal(fixture.Native.RemovalFailure, reply.Failure);
        Assert.Equal(1, fixture.Native.Removals);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Fact]
    public async Task ConfigurationStatusFiveIsNotReinterpretedAsWin32AccessDenied()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Native.RemovalFailure = new(new("ConfigurationManagerFailure", "CM_Request_Device_Eject", 5)) { ConfigurationStatus = 5 };
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal(5u, reply.Failure!.ConfigurationStatus);
        Assert.False(reply.Failure.RequiresAuthorization);
    }

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume)]
    [InlineData(WindowsElevatedOperation.RemoveDevice)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia)]
    public async Task ARejectedNativeOperationDoesNotBecomeSuccessOrTriggerAuthorizationRetry(WindowsElevatedOperation operation)
    {
        var fixture = new Fixture(operation);
        fixture.Native.Error = new("AccessDenied", "NativeOperation", 5, "Permission denied");
        fixture.Native.RemovalFailure = WindowsDeviceRemovalFailure.FromError(fixture.Native.Error);
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal(5, reply.Failure!.Error.NativeCode);
        Assert.True(reply.Failure.RequiresAuthorization);
        Assert.Null(reply.Failure.ConfigurationStatus);
        Assert.Equal(1, fixture.Native.Mounts + fixture.Native.Removals + fixture.Native.OpticalEjections);
    }

    [Fact]
    public async Task NativeAcceptanceWithoutObservedChangeCannotFabricateCompletion()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Native.ObserveMutation = false;
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal("RemovalIncomplete", reply.Failure!.Error.Code);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("inventory")]
    [InlineData("lock")]
    public async Task CancellationNeverReturnsSuccessAndReleasesHandles(string stage)
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        using var cancellation = new CancellationTokenSource();
        if (stage == "before") cancellation.Cancel();
        if (stage == "inventory") fixture.Inventory.BeforeRead = _ => cancellation.Cancel();
        if (stage == "lock") fixture.Native.OnLock = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Host.ExecuteAsync(fixture.Request, cancellation.Token));
        Assert.Equal(0, fixture.Native.Removals);
        Assert.Equal(0, fixture.Native.ActiveHandles);
    }

    [Fact]
    public async Task KnownNativeExceptionsAreStructuredAndDoNotExposeExceptionText()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Inventory.BeforeRead = _ => throw new Win32Exception(5, "untrusted exception payload");
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal(5, reply.Failure!.Error.NativeCode);
        Assert.DoesNotContain("untrusted exception payload", System.Text.Encoding.UTF8.GetString(WindowsDeviceAuthorization.EncodeReply(reply)));
    }

    [Fact]
    public async Task LongNativeDiagnosticsRemainBoundedAndKeepStructuredStatus()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        fixture.Native.RemovalFailure = new(new("RemovalVetoed", "CM_Request_Device_Eject", 0x17, new string('é', 3000) + "\0"))
        { ConfigurationStatus = 0x17, VetoType = 5, VetoName = string.Concat(Enumerable.Repeat("😀", 200)) };
        var reply = await fixture.Host.ExecuteAsync(fixture.Request);
        Assert.False(reply.Completed);
        Assert.Equal(0x17u, reply.Failure!.ConfigurationStatus);
        Assert.Equal(2048, reply.Failure.Error.Message!.Length);
        Assert.InRange(reply.Failure.VetoName!.Length, 1, 260);
        Assert.Equal(reply, WindowsDeviceAuthorization.DecodeReply(WindowsDeviceAuthorization.EncodeReply(reply), fixture.Request));
    }

    [Fact]
    public async Task InvalidRequestsNeverReadInventoryOrReachAnOperation()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Host.ExecuteAsync(fixture.Request with { Operation = (WindowsElevatedOperation)99 }));
        Assert.Equal(0, fixture.Inventory.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedEntrySendsOneReplyClosesChannelAndReturnsExitStatus(bool failure)
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        if (failure) fixture.Native.RemovalFailure = new(new("RemovalVetoed", "CM_Request_Device_Eject", 0x17))
        { ConfigurationStatus = 0x17, VetoType = 5, VetoName = "Busy device" };
        using var ui = new Peer(false);
        using var host = new Peer(true);
        var pipe = new Pipe(ui.Identity, WindowsDeviceAuthorization.EncodeRequest(fixture.Request));
        using var channel = new WindowsOperationIpc(pipe, host, RequestId, false);
        var status = await fixture.Host.RunAsync(channel, ui, TimeSpan.FromSeconds(2));
        Assert.Equal(failure ? 1 : 0, status);
        var reply = WindowsDeviceAuthorization.DecodeReply(pipe.Data.Output.ToArray(), fixture.Request);
        Assert.Equal(!failure, reply.Completed);
        Assert.True(pipe.Disposed);
        Assert.True(pipe.ObservedPeer!.Disposed);
        Assert.Equal(1, fixture.Native.Removals);
    }

    [Fact]
    public async Task UnauthenticatedEntryCannotEnumerateOrExecuteAndStillClosesTheChannel()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        using var ui = new Peer(false);
        using var host = new Peer(true);
        var pipe = new Pipe(ui.Identity with { ProcessId = 99 }, WindowsDeviceAuthorization.EncodeRequest(fixture.Request));
        using var channel = new WindowsOperationIpc(pipe, host, RequestId, false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Host.RunAsync(channel, ui, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, fixture.Inventory.Reads);
        Assert.Empty(pipe.Data.Output);
        Assert.True(pipe.Disposed);
    }

    [Fact]
    public async Task FailedReplyDeliveryCannotReturnSuccessfulExitOrExecuteAgain()
    {
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        using var ui = new Peer(false);
        using var host = new Peer(true);
        var pipe = new Pipe(ui.Identity, WindowsDeviceAuthorization.EncodeRequest(fixture.Request));
        pipe.Data.FailWrites = true;
        using var channel = new WindowsOperationIpc(pipe, host, RequestId, false);
        await Assert.ThrowsAsync<IOException>(() => fixture.Host.RunAsync(channel, ui, TimeSpan.FromSeconds(2)));
        Assert.True(pipe.Disposed);
        Assert.Equal(1, fixture.Native.Removals);
        var retryPipe = new Pipe(ui.Identity, WindowsDeviceAuthorization.EncodeRequest(fixture.Request));
        using var retryChannel = new WindowsOperationIpc(retryPipe, host, RequestId, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Host.RunAsync(retryChannel, ui, TimeSpan.FromSeconds(2)));
        Assert.True(retryPipe.Disposed);
        Assert.Equal(1, fixture.Native.Removals);
    }

    [Fact]
    public async Task NativeHostCannotExecuteOnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return; // Never operate on discovered hardware in ordinary tests.
        var fixture = new Fixture(WindowsElevatedOperation.RemoveDevice);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new WindowsDeviceOperationHost().ExecuteAsync(fixture.Request));
    }

    private sealed class Peer : IWindowsOperationProcess
    {
        public WindowsOperationProcessIdentity Identity { get; }
        public bool IsAlive => !Disposed;
        public bool Disposed { get; private set; }
        public Peer(bool elevated) : this(new WindowsOperationProcessIdentity(elevated ? 20u : 10u, elevated ? 200 : 100,
            @"C:\PSPMan\PSPMan.exe", "S-1-5-21-100", 1, elevated)) { }
        public Peer(WindowsOperationProcessIdentity identity) => Identity = identity;
        public void Dispose() => Disposed = true;
    }

    private sealed class Pipe(WindowsOperationProcessIdentity peer, byte[] payload) : IWindowsOperationPipe
    {
        public DuplexStream Data { get; } = new(payload);
        public Stream Stream => Data;
        public bool Disposed { get; private set; }
        public Peer? ObservedPeer { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public IWindowsOperationProcess OpenPeer() => ObservedPeer = new(peer);
        public void Dispose() { Disposed = true; Data.Dispose(); }
    }

    private sealed class DuplexStream(byte[] payload) : MemoryStream(payload, writable: false)
    {
        public List<byte> Output { get; } = [];
        public bool FailWrites { get; set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites) throw new IOException("Disconnected before reply");
            Output.AddRange(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture
    {
        public Inventory Inventory { get; } = new();
        public Native Native { get; }
        public WindowsDeviceOperationHost Host { get; }
        public WindowsOperationRequest Request { get; }
        public Fixture(WindowsElevatedOperation operation)
        {
            var optical = operation == WindowsElevatedOperation.EjectOpticalMedia;
            var number = new WindowsStorageNumber(optical ? 2u : 7u, 1);
            Inventory.Snapshot = new(
                [new(DeviceId, "parent", "device-interface", "Reader", number, WindowsStorageBus.Usb, optical, true, true),
                 new("sibling", "parent", "sibling-interface", "Sibling", new(7, 2), WindowsStorageBus.Usb, false, true, true)],
                [new("volume-interface", number) { VolumeGuidPath = VolumeGuid, FileSystemSerialNumber = 42,
                    State = WindowsVolumeState.Available, MountPoints = operation == WindowsElevatedOperation.MountVolume ? [] : ["E:\\"] }], []);
            Native = new(Inventory);
            Host = new(Inventory, Native, Native, Native);
            Request = new(1, RequestId, operation, DeviceId, number,
                operation == WindowsElevatedOperation.MountVolume ? VolumeGuid : null,
                operation == WindowsElevatedOperation.MountVolume ? 42u : null);
        }
    }

    private sealed class Inventory : IWindowsDeviceApi
    {
        public WindowsStorageSnapshot Snapshot { get; set; } = new([], [], []);
        public Action<int>? BeforeRead { get; set; }
        public int Reads { get; private set; }
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            BeforeRead?.Invoke(++Reads);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Snapshot);
        }
    }

    private sealed class Native(Inventory inventory) : IWindowsVolumeMountApi, IWindowsRemovalNativeApi, IWindowsOpticalNativeApi
    {
        public int Mounts { get; private set; }
        public int Removals { get; private set; }
        public int OpticalEjections { get; private set; }
        public int ActiveHandles { get; private set; }
        public bool ObserveMutation { get; set; } = true;
        public bool MediaPresent { get; set; } = true;
        public Action? OnLock { get; set; }
        public WindowsDeviceError? Error { get; set; }
        public WindowsDeviceRemovalFailure? RemovalFailure { get; set; }
        public ValueTask<WindowsDriveLetterObservation> GetOccupiedDriveLettersAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WindowsDriveLetterObservation(0));
        public ValueTask<WindowsDeviceError?> AssignRootAsync(WindowsVolumeMountTarget target, string root, CancellationToken cancellationToken = default)
        {
            Mounts++;
            Assert.Equal("device-interface", target.InterfacePath);
            if (Error is null && ObserveMutation) inventory.Snapshot = inventory.Snapshot with
            { Volumes = [inventory.Snapshot.Volumes[0] with { MountPoints = [root] }] };
            return ValueTask.FromResult(Error);
        }
        public IWindowsRemovalVolume? OpenVolume(WindowsVolumeReference volume, out WindowsDeviceError? error)
        {
            error = null;
            ActiveHandles++;
            return new Handle(this, volume.StorageNumber);
        }
        public WindowsDeviceRemovalFailure? LocateDevice(string instanceId, out uint node)
        {
            Assert.Equal(DeviceId, instanceId);
            Assert.Equal(0, ActiveHandles);
            node = 42;
            return null;
        }
        public WindowsDeviceRemovalFailure? RequestEject(uint node)
        {
            Assert.Equal(42u, node);
            Assert.Equal(0, ActiveHandles);
            Removals++;
            if (RemovalFailure is null && ObserveMutation) inventory.Snapshot = inventory.Snapshot with
            { Devices = inventory.Snapshot.Devices.Where(device => device.InstanceId != DeviceId).ToImmutableArray(), Volumes = [] };
            return RemovalFailure;
        }
        public IWindowsOpticalHandle? Open(string path, out WindowsDeviceError? error)
        {
            Assert.Contains(path, new[] { "device-interface", VolumeGuid.TrimEnd('\\') });
            error = null;
            ActiveHandles++;
            return new Handle(this, new(2, 1));
        }
        private void ObserveOpticalEjection()
        {
            MediaPresent = false;
            inventory.Snapshot = inventory.Snapshot with { Volumes = [] };
        }
        private sealed class Handle(Native owner, WindowsStorageNumber? number) : IWindowsOpticalHandle
        {
            private bool disposed;
            public WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error) { error = null; return number; }
            public WindowsDeviceError? Lock() { owner.OnLock?.Invoke(); return null; }
            public WindowsOpticalMediaObservation CheckMedia() => new(owner.MediaPresent, 1);
            public WindowsDeviceError? Eject()
            {
                owner.OpticalEjections++;
                if (owner.Error is null && owner.ObserveMutation)
                {
                    owner.ObserveOpticalEjection();
                }
                return owner.Error;
            }
            public void Dispose() { if (!disposed) { disposed = true; owner.ActiveHandles--; } }
        }
    }
}
