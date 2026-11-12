using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class DeviceCatalogBoundaryTests
{
    [Theory]
    [InlineData(MountedPathSyntax.Linux)]
    [InlineData(MountedPathSyntax.Windows)]
    public async Task AdditionalProviderPreservesAssociationsWithoutCatalogAccessOrNativeOperations(MountedPathSyntax syntax)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var records = new[] { Game(GameMedium.Usb), Game(GameMedium.MemoryCard), Game(GameMedium.Dvd, 42), Game(GameMedium.Dvd, 99) };
            var repository = new Repository(records);
            var platform = new Provider("platform", Device("platform", DeviceMedium.Usb, DeviceCapabilities.RemovableSource, syntax),
                Device("platform", DeviceMedium.Optical, DeviceCapabilities.OpticalSource, syntax));
            var consoles = new Provider("consoles", Device("consoles", DeviceMedium.Psp, DeviceCapabilities.PspDestination, syntax));
            // Unknown informative medium, existing removable capability, no new GameMedium or network adapter.
            var additional = new Provider("additional", Device("additional", DeviceMedium.Unknown, DeviceCapabilities.RemovableSource, syntax));
            await using (var startup = await DeviceStartup.CreateAsync([platform, consoles, additional]))
            {
                using var devices = new DevicesViewModel(startup.Session!, action => Dispatcher.UIThread.Post(action));
                var main = new MainViewModel(new GameCatalogService(repository), null, devicesViewModel: devices, mountedPathSyntax: syntax);
                repository.Sealed = true;
                await startup.StartAsync();
                await Until(() => devices.Devices.Length == 4);
                main.SelectedGame = records[0];
                await Until(() => devices.RemovableSource.Status == SourceResolutionStatus.ChooseDevice);
                Assert.Equal(3, startup.Session!.Snapshot.ProviderRevisions.Count);
                Assert.Equal(2, devices.RemovableSource.Candidates.Length);
                await devices.ChooseSourceCommand.ExecuteAsync(devices.RemovableSource.Candidates.Single(choice => choice.Device.ProviderId == "additional"));
                await Until(() => devices.RemovableSource.SelectedDevice?.ProviderId == "additional");
                Assert.Equal(DestinationResolutionStatus.Selected, devices.GameDestination.Status);
                Assert.Single(devices.GameDestination.Candidates); // Source labels named PSP cannot create destinations.
                foreach (var game in records)
                {
                    main.SelectedGame = game;
                    await Until(() => devices.GameDestination.GameId == game.Id);
                    Assert.Equal(SourceResolutionStatus.Selected, game.Medium == GameMedium.Dvd ? devices.OpticalSource.Status : devices.RemovableSource.Status);
                    var root = syntax == MountedPathSyntax.Linux ? "/synthetic/" : @"R:\";
                    Assert.Equal(root + (syntax == MountedPathSyntax.Linux ? "GAMES/Keep.rar" : @"GAMES\Keep.rar"), devices.ConstructedSourcePath);
                    Assert.Equal("consoles", devices.GameDestination.SelectedDevice!.Value.ProviderId);
                }
                Assert.Equal("additional", devices.RemovableSource.PreferredDevice!.Value.ProviderId);
                Assert.Null(devices.OpticalSource.PreferredDevice);
                Assert.Equal(records, main.Games);
                Assert.Equal(0, repository.ForbiddenCalls);
            }
            foreach (var provider in new[] { platform, consoles, additional })
            {
                Assert.Equal(0, provider.Operations);
                Assert.Equal(1, provider.Watches); Assert.Equal(1, provider.Unsubscriptions); Assert.Equal(1, provider.Disposals);
            }
        });
    }

    private static GameRecord Game(GameMedium medium, int? disc = null) =>
        new(Guid.NewGuid(), medium.ToString(), medium, disc, "/GAMES/Keep.rar", GameFormat.Iso, 1, null);
    private static DeviceSnapshot Device(string provider, DeviceMedium medium, DeviceCapabilities capability, MountedPathSyntax syntax)
    {
        // Reuse local device/volume IDs across providers to exercise provider-scoped identity.
        var key = new DeviceKey(provider, medium == DeviceMedium.Optical ? "optical" : "same-local-id");
        var identity = new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly);
        return new(key, "PSP", medium, capability | DeviceCapabilities.Mount | DeviceCapabilities.SafeRemoval,
            identity, "media", DeviceState.Ready, [new(new(key, "volume"), "PSP", syntax == MountedPathSyntax.Linux
                ? "/synthetic/" : @"R:\", identity, DeviceState.Ready)]);
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { Dispatcher.UIThread.RunJobs(); if (condition()) return; await Task.Delay(10, timeout.Token); }
    }
    private sealed class Repository(GameRecord[] records) : IGameRecordRepository
    {
        public bool Sealed;
        public int ForbiddenCalls;
        public IReadOnlyList<GameRecord> List() { Check(); return records.ToArray(); }
        public GameRecord? GetById(Guid id) { Check(); return records.SingleOrDefault(record => record.Id == id); }
        private void Check() { if (Sealed) { ForbiddenCalls++; throw new InvalidOperationException("Device actions must not read the catalog."); } }
        private void Reject() { ForbiddenCalls++; throw new InvalidOperationException("Device actions must not mutate the catalog."); }
        public void Create(GameRecord record) => Reject();
        public void Update(GameRecord record) => Reject();
        public void Delete(Guid id) => Reject();
    }
    private sealed class Provider(string id, params DeviceSnapshot[] devices) : IDeviceProvider
    {
        public string ProviderId => id;
        public int Operations, Watches, Unsubscriptions, Disposals;
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(new DeviceInventorySnapshot(1, [.. devices])); }
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            Watches++;
            try { yield return await GetSnapshotAsync(token); await Task.Delay(Timeout.Infinite, token); }
            finally { Unsubscriptions++; }
        }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => Reject();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default) => Reject();
        private ValueTask<DeviceOperationResult> Reject()
        { Interlocked.Increment(ref Operations); throw new InvalidOperationException("Already mounted boundary fixtures must not invoke native operations."); }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
