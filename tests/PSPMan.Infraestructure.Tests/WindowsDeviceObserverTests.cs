using System.Collections.Immutable;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceObserverTests
{
    [Fact]
    public async Task BuffersArrivalDuringInitialScanWithoutBlockingCallback()
    {
        var api = new Notifications();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async ValueTask<WindowsDeviceInventory> Read(CancellationToken token)
        {
            Assert.Equal(3, api.Interfaces);
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new([], []);
            }
            return Snapshot("E:\\");
        }
        await using var observer = new WindowsDeviceObserver(Read, api);
        await using var stream = observer.ObserveAsync().GetAsyncEnumerator();
        var first = stream.MoveNextAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(api.Signal).WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(stream.Current.Devices);
        Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(stream.Current.Devices);
    }

    [Fact]
    public async Task WatchesVolumesClosesRegistrationGapAndPublishesRootChangesAndRemoval()
    {
        var api = new Notifications();
        var snapshot = Snapshot("E:\\");
        api.OnVolume = () => snapshot = Snapshot("F:\\");
        await using var observer = new WindowsDeviceObserver(_ => ValueTask.FromResult(snapshot), api);
        await using var stream = observer.ObserveAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("F:\\", Assert.Single(Assert.Single(stream.Current.Devices).Volumes).MountPoints.Single());
        snapshot = new([], []);
        api.Signal();
        Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(stream.Current.Devices);
        Assert.Equal(3, api.Active);
        await observer.DisposeAsync();
        Assert.Equal(0, api.Active);
    }

    [Fact]
    public async Task CoalescesBurstsSuppressesEquivalentSnapshotsAndCancelsPendingRead()
    {
        var api = new Notifications();
        var reads = 0;
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot = Snapshot("E:\\");
        ValueTask<WindowsDeviceInventory> Read(CancellationToken token)
        {
            var current = snapshot;
            if (Interlocked.Increment(ref reads) == 2) secondRead.SetResult();
            return ValueTask.FromResult(current with { Devices = current.Devices.ToArray().ToImmutableArray() });
        }
        await using var observer = new WindowsDeviceObserver(Read, api);
        using var cancellation = new CancellationTokenSource();
        await using var stream = observer.ObserveAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        for (var i = 0; i < 1000; i++) api.Signal();
        var next = stream.MoveNextAsync().AsTask();
        // The registration-gap signal and the burst occupy one bounded queue slot.
        await secondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        snapshot = Snapshot("F:\\");
        api.Signal();
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("F:\\", Assert.Single(Assert.Single(stream.Current.Devices).Volumes).MountPoints.Single());
        Assert.Equal(3, reads);
        var pending = stream.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, api.Active);
    }

    [Fact]
    public async Task InitialInventoryFailureReleasesRegistrations()
    {
        var api = new Notifications();
        await using var observer = new WindowsDeviceObserver(_ => throw new IOException("scan"), api);
        await using var stream = observer.ObserveAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<IOException>(() => stream.MoveNextAsync().AsTask());
        Assert.Equal(0, api.Active);
    }

    [Fact]
    public async Task PublishesUnavailableVolumeAndNativeErrorWithoutRetainingUsableRoots()
    {
        var api = new Notifications();
        var snapshot = Snapshot("E:\\");
        await using var observer = new WindowsDeviceObserver(_ => ValueTask.FromResult(snapshot), api);
        await using var stream = observer.ObserveAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var device = Assert.Single(snapshot.Devices);
        var volume = Assert.Single(device.Volumes);
        snapshot = snapshot with { Devices = [device with { Volumes = [volume with
        {
            State = WindowsVolumeState.Unavailable, MountPoints = [],
            Error = new("NativeFailure", "ReadVolume", 21)
        }] }] };
        api.Signal();
        Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        var unavailable = Assert.Single(Assert.Single(stream.Current.Devices).Volumes);
        Assert.False(unavailable.IsMounted);
        Assert.Empty(unavailable.MountPoints);
        Assert.Equal(21, unavailable.Error?.NativeCode);
    }

    [Fact]
    public async Task CancellationDuringInitialScanAndEnumeratorDisposalReleaseSubscriptions()
    {
        var api = new Notifications();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<WindowsDeviceInventory> Read(CancellationToken token)
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new([], []);
        }
        await using var observer = new WindowsDeviceObserver(Read, api);
        using var cancellation = new CancellationTokenSource();
        await using var stream = observer.ObserveAsync(cancellation.Token).GetAsyncEnumerator();
        var pending = stream.MoveNextAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, api.Active);

        var nextApi = new Notifications();
        await using var next = new WindowsDeviceObserver(_ => ValueTask.FromResult(Snapshot("E:\\")), nextApi);
        var enumerator = next.ObserveAsync().GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync();
        Assert.Equal(0, nextApi.Active);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task PartialRegistrationFailureReleasesEarlierRegistrations(int failAt)
    {
        var api = new Notifications { FailAt = failAt };
        await using var observer = new WindowsDeviceObserver(_ => ValueTask.FromResult(Snapshot("E:\\")), api);
        await using var stream = observer.ObserveAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<IOException>(() => stream.MoveNextAsync().AsTask());
        Assert.Equal(0, api.Active);
    }

    [Fact]
    public async Task DisposalDuringScanReleasesWatchesAndRejectsLateResult()
    {
        var api = new Notifications();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WindowsDeviceInventory>(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<WindowsDeviceInventory> Read(CancellationToken _)
        {
            entered.SetResult();
            return await release.Task;
        }
        await using var observer = new WindowsDeviceObserver(Read, api);
        await using var stream = observer.ObserveAsync().GetAsyncEnumerator();
        var next = stream.MoveNextAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await observer.DisposeAsync();
        Assert.Equal(0, api.Active);
        release.SetResult(Snapshot("E:\\"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, api.Active);
    }

    [Fact]
    public void NativeNotificationsAreGuardedOutsideWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        var api = new WindowsDeviceNotificationApi();
        Assert.Throws<PlatformNotSupportedException>(() => api.RegisterInterface(Guid.Empty, () => { }));
        Assert.Throws<PlatformNotSupportedException>(() => api.RegisterVolume("volume", () => { }));
    }

    private static WindowsDeviceInventory Snapshot(string root) => new(
        [new("device", null, "disk", "Reader", WindowsDeviceKind.Usb, true, true,
            [new("volume", new(7, 1)) { VolumeGuidPath = @"\\?\Volume{00000000-0000-0000-0000-000000000001}\",
                State = WindowsVolumeState.Available, MountPoints = [root] }], null)], []);

    private sealed class Notifications : IWindowsDeviceNotificationApi
    {
        private Action? _callback;
        public int Interfaces { get; private set; }
        public int Active { get; private set; }
        public int FailAt { get; init; }
        public Action? OnVolume { get; set; }
        private int _attempts;
        public IDisposable RegisterInterface(Guid classGuid, Action callback)
        {
            Interfaces++;
            return Register(callback);
        }
        public IDisposable RegisterVolume(string path, Action callback)
        {
            var registration = Register(callback);
            OnVolume?.Invoke();
            return registration;
        }
        private IDisposable Register(Action callback)
        {
            if (++_attempts == FailAt) throw new IOException("register");
            Active++;
            _callback = callback;
            return new Registration(() => Active--);
        }
        public void Signal() => _callback?.Invoke();
        private sealed class Registration(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
