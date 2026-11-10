using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxVolumeMounterTests
{
    [Fact]
    public async Task ReusesAnExistingMountWithoutCallingUDisks()
    {
        var client = new FakeMountClient("/media/games");
        var mounter = new LinuxVolumeMounter(client);
        var volume = Volume("/media/games");

        var root = await mounter.MountAsync(volume);

        Assert.Equal("/media/games", root);
        Assert.Equal(0, client.MountCalls);
    }

    [Fact]
    public async Task RequestsMountAndReturnsTheNativeRoot()
    {
        var client = new FakeMountClient("/run/media/user/games");
        var mounter = new LinuxVolumeMounter(client);
        var volume = Volume();

        var root = await mounter.MountAsync(volume);

        Assert.Equal("/run/media/user/games", root);
        Assert.Equal(1, client.MountCalls);
        Assert.Equal(volume.ObjectPath, client.LastObjectPath);
    }

    [Fact]
    public async Task PreservesAuthorizationFailureWithoutRetryingOrInventingAReadyRoot()
    {
        var client = new FakeMountClient(exception: new UDisksClientException(
            "AuthorizationDenied", "The authorization request was denied."));
        var mounter = new LinuxVolumeMounter(client);

        var error = await Assert.ThrowsAsync<UDisksClientException>(() =>
            mounter.MountAsync(Volume()).AsTask());

        Assert.Equal("AuthorizationDenied", error.Code);
        Assert.Equal(1, client.MountCalls);
    }

    [Fact]
    public async Task DoesNotCallUDisksWhenCancellationWasRequested()
    {
        var client = new FakeMountClient("/media/games");
        var mounter = new LinuxVolumeMounter(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mounter.MountAsync(Volume(), cancellation.Token).AsTask());

        Assert.Equal(0, client.MountCalls);
    }

    private static LinuxVolume Volume(string mountPoint = "") => new()
    {
        ObjectPath = "/org/freedesktop/UDisks2/block_devices/sdb1",
        DevicePath = "/dev/sdb1",
        Label = "Games",
        MountPoints = string.IsNullOrEmpty(mountPoint) ? [] : [mountPoint]
    };

    private sealed class FakeMountClient(
        string? mountPoint = null,
        UDisksClientException? exception = null) : IUDisksClient
    {
        public int MountCalls { get; private set; }
        public string? LastObjectPath { get; private set; }

        public ValueTask<UDisksObjectManagerSnapshot> GetManagedObjectsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new UDisksObjectManagerSnapshot([]));

        public ValueTask<string> MountAsync(string filesystemObjectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MountCalls++;
            LastObjectPath = filesystemObjectPath;
            if (exception is not null)
                throw exception;
            return ValueTask.FromResult(mountPoint!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
