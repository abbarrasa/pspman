using System.Runtime.CompilerServices;
using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Infraestructure.Tests;

public sealed class DeviceProviderFactoryTests
{
    [Theory]
    [InlineData(DevicePlatform.Linux, "linux")]
    [InlineData(DevicePlatform.Windows, "windows")]
    public async Task ConstructsOnlyTheSelectedPlatform(DevicePlatform platform, string expected)
    {
        var calls = new List<string>();
        ValueTask<IDeviceProvider> Create(string id) { calls.Add(id); return ValueTask.FromResult<IDeviceProvider>(new Provider(id)); }
        var result = await DeviceProviderFactory.CreateAsync(platform, _ => Create("linux"), _ => Create("windows"));
        Assert.Null(result.Error);
        Assert.Equal(expected, Assert.Single(result.Providers).ProviderId);
        Assert.Equal([expected], calls);
        await result.Providers[0].DisposeAsync();
    }

    [Fact]
    public async Task UnsupportedPlatformDoesNotConstructAnyNativeProvider()
    {
        var result = await DeviceProviderFactory.CreateAsync(DevicePlatform.Unsupported,
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        Assert.Empty(result.Providers);
        Assert.Equal("UnsupportedPlatform", result.Error!.Code);
    }

    [Fact]
    public async Task ServiceFailureIsObservableAndDoesNotFallBackToAnotherPlatform()
    {
        var result = await DeviceProviderFactory.CreateAsync(DevicePlatform.Linux,
            _ => throw new IOException("Unavailable"), _ => throw new InvalidOperationException("Wrong platform"));
        Assert.Empty(result.Providers);
        Assert.Equal("DeviceServiceUnavailable", result.Error!.Code);
    }

    [Fact]
    public async Task CancellationIsNotAnUnavailableResultAndDoesNotConstructAProvider()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await DeviceProviderFactory.CreateAsync(DevicePlatform.Linux,
                _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), cancelled.Token));
    }

    [Fact]
    public async Task CancellationAfterConstructionDisposesTheLateProvider()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new Provider("linux");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await DeviceProviderFactory.CreateAsync(DevicePlatform.Linux, _ =>
            {
                cancellation.Cancel(); return ValueTask.FromResult<IDeviceProvider>(provider);
            }, _ => throw new InvalidOperationException(), cancellation.Token));
        Assert.Equal(1, provider.Disposals);
    }

    [Fact]
    public void ExplicitProvidersAcceptAnAdditionalInMemoryProviderAndRejectDuplicateIds()
    {
        var providers = new IDeviceProvider[] { new Provider("linux"), new Provider("memory") };
        var result = DeviceProviderFactory.FromProviders(providers);
        Assert.Equal(providers, result.Providers);
        Assert.Null(result.Error);
        Assert.Throws<ArgumentException>(() => DeviceProviderFactory.FromProviders([providers[0], providers[0]]));
        Assert.Throws<ArgumentException>(() => DeviceProviderFactory.FromProviders([new Provider("")]));
    }

    private sealed class Provider(string id) : IDeviceProvider
    {
        public int Disposals;
        public string ProviderId => id;
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) => new(new DeviceInventorySnapshot(0, []));
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { await Task.CompletedTask; yield break; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
