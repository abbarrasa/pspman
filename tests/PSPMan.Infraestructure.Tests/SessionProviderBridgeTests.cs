using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Infraestructure.Tests;

public sealed class SessionProviderBridgeTests
{
    [Fact]
    public async Task IdenticalObservationsRetainRevisionAndRootChangesAdvanceIt()
    {
        await using var provider = new Bridge();
        var first = await provider.GetSnapshotAsync();
        Assert.Equal(first.Revision, (await provider.GetSnapshotAsync()).Revision);
        provider.Device = provider.Device with { DisplayName = "Changed" };
        Assert.True((await provider.GetSnapshotAsync()).Revision > first.Revision);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("provider")]
    [InlineData("generation")]
    [InlineData("refresh")]
    public async Task RejectsStaleRequestsWithoutCallingNativeOperations(string mismatch)
    {
        await using var provider = new Bridge();
        var first = await provider.GetSnapshotAsync();
        var request = new DeviceRemovalRequest(provider.Device.Key, first.Revision, "generation");
        if (mismatch == "revision") request = request with { ExpectedRevision = 0 };
        if (mismatch == "provider") request = request with { Device = new("other", "device") };
        if (mismatch == "generation") request = request with { MediaGeneration = "changed" };
        if (mismatch == "refresh") provider.Device = provider.Device with { MediaGeneration = "changed" };
        Assert.Equal(DeviceOperationStatus.StaleObservation, (await provider.RemoveAsync(request)).Status);
        Assert.Equal(0, provider.Operations);
    }

    [Fact]
    public async Task AnUnrelatedDevicesRevisionDoesNotInvalidateAStillCurrentSelection()
    {
        await using var provider = new Bridge { IncludeOther = true };
        var first = await provider.GetSnapshotAsync();
        provider.OtherName = "Changed sibling";
        Assert.True((await provider.GetSnapshotAsync()).Revision > first.Revision);
        Assert.Equal(DeviceOperationStatus.Succeeded,
            (await provider.RemoveAsync(new(provider.Device.Key, first.Revision, "generation"))).Status);
    }

    [Fact]
    public async Task ValidRequestDelegatesAndDisposalIsIdempotentAndPreventsLateCalls()
    {
        var provider = new Bridge();
        var first = await provider.GetSnapshotAsync();
        Assert.Equal(DeviceOperationStatus.Succeeded,
            (await provider.RemoveAsync(new(provider.Device.Key, first.Revision, "generation"))).Status);
        Assert.Equal(1, provider.Operations);
        await provider.DisposeAsync(); await provider.DisposeAsync();
        Assert.Equal(1, provider.Disposals);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.GetSnapshotAsync());
    }

    private sealed class Bridge : SessionProviderBridge<DeviceSnapshot>
    {
        public DeviceSnapshot Device = new(new("test", "device"), "Device", DeviceMedium.Usb,
            DeviceCapabilities.RemovableSource, new(null, DeviceIdentityConfidence.SessionOnly), "generation", DeviceState.Ready, []);
        public int Operations, Disposals;
        public bool IncludeOther;
        public string OtherName = "Other";
        public override string ProviderId => "test";
        protected override ValueTask<DeviceSnapshot> ReadNativeAsync(CancellationToken token) => new(Device);
        protected override ImmutableArray<DeviceSnapshot> Project(DeviceSnapshot inventory) => IncludeOther
            ? [inventory, inventory with { Key = new("test", "other"), DisplayName = OtherName }] : [inventory];
        protected override async IAsyncEnumerable<DeviceSnapshot> ObserveNativeAsync([EnumeratorCancellation] CancellationToken token)
        { yield return Device; await Task.Delay(Timeout.Infinite, token); }
        protected override ValueTask<DeviceOperationResult> MountNativeAsync(DeviceSnapshot inventory, DeviceMountRequest request, CancellationToken token) => throw new InvalidOperationException();
        protected override ValueTask<DeviceOperationResult> RemoveNativeAsync(DeviceSnapshot inventory, DeviceRemovalRequest request, CancellationToken token)
        { Operations++; return new(new DeviceOperationResult(DeviceOperationStatus.Succeeded)); }
        protected override ValueTask DisposeNativeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
