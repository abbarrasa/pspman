using PSPMan.Infraestructure.Devices.Windows;
using Xunit.Abstractions;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Uses real read-only Windows APIs. Never constructs a mount/removal/authorization service.</summary>
[Trait("Category", "WindowsNativeReadOnly")]
public sealed class WindowsNativeReadOnlyIntegrationTests(ITestOutputHelper output)
{
    public sealed class WindowsNativeFactAttribute : FactAttribute
    {
        public WindowsNativeFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Requires Windows native APIs; Windows native verification remains pending on this platform.";
        }
    }

    private static readonly Guid[] InterfaceClasses =
    [
        new("53f56307-b6bf-11d0-94f2-00a0c91efb8b"),
        new("53f56308-b6bf-11d0-94f2-00a0c91efb8b"),
        new("53f5630d-b6bf-11d0-94f2-00a0c91efb8b")
    ];

    [Fact]
    public void NativeChecksAreExplicitlySkippedOutsideWindows()
    {
        var attribute = new WindowsNativeFactAttribute();
        if (OperatingSystem.IsWindows()) Assert.Null(attribute.Skip);
        else Assert.Contains("Windows native verification remains pending", attribute.Skip);
    }

    [Fact]
    public async Task ForeignPlatformCallsFailBeforeLoadingNativeLibraries()
    {
        if (OperatingSystem.IsWindows()) return;
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await new WindowsDeviceApi().EnumerateStorageAsync());
        var api = new WindowsDeviceNotificationApi();
        Assert.Throws<PlatformNotSupportedException>(() => api.RegisterInterface(InterfaceClasses[0], () => { }));
        Assert.Throws<PlatformNotSupportedException>(() => api.RegisterVolume("unused", () => { }));
    }

    [WindowsNativeFact]
    public async Task EnumeratesRealStorageAndPreservesStructuredErrorsWithoutProbingFiles()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var api = new WindowsDeviceApi();
        var snapshot = await api.EnumerateStorageAsync(deadline.Token);
        Assert.False(snapshot.Devices.IsDefault);
        Assert.False(snapshot.Volumes.IsDefault);
        Assert.False(snapshot.Errors.IsDefault);
        Assert.All(snapshot.Devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.InstanceId));
            Assert.False(string.IsNullOrWhiteSpace(device.InterfacePath));
        });
        Assert.All(snapshot.Volumes, volume =>
        {
            Assert.False(string.IsNullOrWhiteSpace(volume.InterfacePath));
            Assert.All(volume.MountPoints, root => Assert.True(Path.IsPathFullyQualified(root)));
            if (volume.IsMounted) Assert.Equal(WindowsVolumeState.Available, volume.State);
        });
        Assert.All(snapshot.Errors, error =>
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Code));
            Assert.False(string.IsNullOrWhiteSpace(error.Operation));
            // Do not print native messages, serials, instance identifiers, labels or roots.
            output.WriteLine($"Native error: {error.Operation}; {error.Code}; {error.NativeCode}");
        });
        // A failed global scan is not an empty successful inventory. Per-device access/no-media
        // failures are legitimate structured observations, not reasons to skip a native test.
        Assert.DoesNotContain(snapshot.Errors, error => error.Operation is "SetThreadErrorMode"
            or "SetupDiGetClassDevs" or "SetupDiEnumDeviceInterfaces" or "FindFirstVolume" or "FindNextVolume");
        output.WriteLine($"Devices: {snapshot.Devices.Length}; volumes: {snapshot.Volumes.Length}; errors: {snapshot.Errors.Length}.");
        // Cancellation is deterministic and must not require a device or an OS mutation.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await api.EnumerateStorageAsync(cancelled.Token));
    }

    [WindowsNativeFact]
    public void RegistersEachNativeInterfaceAndDisposesIdempotently()
    {
        var api = new WindowsDeviceNotificationApi();
        foreach (var classGuid in InterfaceClasses)
        {
            using var registration = api.RegisterInterface(classGuid, () => { });
            Assert.NotNull(registration);
            registration.Dispose();
            registration.Dispose();
        }
    }

    [WindowsNativeFact]
    public async Task RealObserverRegistersBeforeInventoryAndReleasesAllAcquiredRegistrations()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var notifications = new TrackedNotifications();
        var provider = new WindowsDeviceProvider(new WindowsDeviceApi());
        await using var observer = new WindowsDeviceObserver(token =>
        {
            Assert.Equal(3, notifications.InterfaceCount);
            return provider.GetInventoryAsync(token);
        }, notifications);
        await using (var observations = observer.ObserveAsync(deadline.Token).GetAsyncEnumerator())
        {
            Assert.True(await observations.MoveNextAsync());
            var inventory = observations.Current;
            Assert.All(inventory.Devices.SelectMany(device => device.Volumes), volume =>
            {
                if (volume.State != WindowsVolumeState.Available || volume.Error is not null)
                    Assert.Empty(volume.MountPoints);
            });
            var expectedVolumes = inventory.Devices.SelectMany(device => device.Volumes)
                .Select(volume => volume.VolumeGuidPath).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Assert.Equal(expectedVolumes, notifications.VolumeCount);
            output.WriteLine($"Native volume watches acquired: {notifications.VolumeCount}. Zero means volume-watch hardware coverage remains pending.");
        }
        await observer.DisposeAsync();
        Assert.Equal(notifications.Acquired, notifications.Released);
        Assert.Equal(3, notifications.InterfaceCount);
    }

    [WindowsNativeFact]
    public async Task InventoryFailureStillReleasesRealInterfaceRegistrations()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var notifications = new TrackedNotifications();
        await using var observer = new WindowsDeviceObserver(_ => throw new IOException("Injected inventory failure"), notifications);
        await using var observations = observer.ObserveAsync(deadline.Token).GetAsyncEnumerator();
        await Assert.ThrowsAsync<IOException>(async () => await observations.MoveNextAsync());
        Assert.Equal(3, notifications.Acquired);
        Assert.Equal(notifications.Acquired, notifications.Released);
    }

    private sealed class TrackedNotifications : IWindowsDeviceNotificationApi
    {
        private readonly WindowsDeviceNotificationApi _native = new();
        public int InterfaceCount { get; private set; }
        public int VolumeCount { get; private set; }
        public int Acquired => InterfaceCount + VolumeCount;
        public int Released { get; private set; }

        public IDisposable RegisterInterface(Guid classGuid, Action changed)
        {
            var registration = _native.RegisterInterface(classGuid, changed);
            InterfaceCount++;
            return new Lease(registration, () => Released++);
        }

        public IDisposable RegisterVolume(string path, Action changed)
        {
            var registration = _native.RegisterVolume(path, changed);
            VolumeCount++;
            return new Lease(registration, () => Released++);
        }

        private sealed class Lease(IDisposable native, Action released) : IDisposable
        {
            private IDisposable? _native = native;
            public void Dispose()
            {
                var owned = Interlocked.Exchange(ref _native, null);
                if (owned is null) return;
                owned.Dispose();
                released();
            }
        }
    }
}
