using System.Runtime.CompilerServices;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Desktop.Tests;

public sealed class DeviceStartupTests
{
    [Fact]
    public async Task CompositionDoesNotStartDiscoveryAndDisposesUnstartedProviders()
    {
        var provider = new Provider("memory");
        await using (var startup = await DeviceStartup.CreateAsync([provider]))
        {
            Assert.NotNull(startup.Session);
            Assert.Null(startup.Error);
            Assert.Equal(0, provider.Reads);
            Assert.Equal(0, provider.Watches);
        }
        Assert.Equal(1, provider.Disposals);
    }

    [Fact]
    public async Task ThirdProviderUsesTheExistingSessionWithoutAssociationRuleChanges()
    {
        var first = new Provider("platform"); var extra = new Provider("memory") { HasSource = true };
        await using var startup = await DeviceStartup.CreateAsync([first, extra]);
        await startup.StartAsync();
        Assert.Null(startup.Error);
        Assert.Equal(2, startup.Session!.Snapshot.ProviderRevisions.Count);
        Assert.Empty(startup.Session.Snapshot.ProviderErrors);
        Assert.Equal(1, first.Watches); Assert.Equal(1, extra.Watches);
        var source = DeviceAssociationRules.ResolveSource(GameMedium.MemoryCard, startup.Session.Snapshot.Inventory);
        Assert.Equal(SourceResolutionStatus.Selected, source.Status);
        Assert.Equal("memory", source.SelectedDevice!.Value.ProviderId);
    }

    [Fact]
    public async Task CompositionFailureDoesNotPreventOfflineCatalogStartup()
    {
        await using var startup = await DeviceStartup.CreateAsync(createProviders: _ =>
            ValueTask.FromResult(new DeviceProviderFactoryResult([], new("DeviceServiceUnavailable", "Device services are unavailable."))));
        Assert.Null(startup.Session);
        Assert.Equal("DeviceServiceUnavailable", startup.Error!.Code);
        await startup.StartAsync();
        var directory = Directory.CreateTempSubdirectory("PSPMan-T37-");
        try
        {
            var catalog = CatalogStartup.CreateMainViewModel(directory.FullName);
            Assert.True(catalog.IsCatalogAvailable);
            Assert.True(catalog.CatalogService!.Create(new("Offline", GameMedium.Usb, null, "/offline.iso", GameFormat.Iso, 1, null)).IsSuccess);
            Assert.Single(CatalogStartup.CreateMainViewModel(directory.FullName).Games);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task ProviderObservationFailureRemainsObservableInTheSession()
    {
        var provider = new Provider("broken") { Fail = true };
        await using var startup = await DeviceStartup.CreateAsync([provider]);
        await startup.StartAsync();
        Assert.Contains("broken", startup.Session!.Snapshot.ProviderErrors.Keys);
        Assert.Empty(startup.Session.Snapshot.Inventory.Devices);
    }

    [Fact]
    public async Task ThrownInitializationFailureIsObservableWithoutACatalogDependency()
    {
        await using var startup = await DeviceStartup.CreateAsync(createProviders: _ => throw new IOException("Unavailable"));
        Assert.Null(startup.Session);
        Assert.Equal("DeviceServiceUnavailable", startup.Error!.Code);
    }

    [Fact]
    public async Task CancellationIsNotConvertedIntoAServiceFailure()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await DeviceStartup.CreateAsync([], cancellationToken: cancellation.Token));
    }

    private sealed class Provider(string id) : IDeviceProvider
    {
        public string ProviderId => id;
        public int Reads, Watches, Disposals;
        public bool Fail, HasSource;
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default)
        {
            Reads++; if (Fail) throw new IOException("Inventory unavailable");
            var key = new DeviceKey(id, "device");
            var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
            return new(new DeviceInventorySnapshot(1, HasSource ? [new(key, "Memory source", DeviceMedium.Usb,
                DeviceCapabilities.RemovableSource, identity, "generation", DeviceState.Ready,
                [new(new(key, "volume"), null, "/memory", identity, DeviceState.Ready)])] : []));
        }
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { Watches++; yield return await GetSnapshotAsync(token); await Task.Delay(Timeout.Infinite, token); }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => throw new InvalidOperationException();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
