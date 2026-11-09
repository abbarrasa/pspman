using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public sealed class DeviceAssociationRulesTests
{
    [Theory]
    [InlineData(GameMedium.Dvd, SourceGroup.Optical)]
    [InlineData(GameMedium.Usb, SourceGroup.Removable)]
    [InlineData(GameMedium.MemoryCard, SourceGroup.Removable)]
    public void GroupsOpticalSeparatelyFromUsbAndCards(GameMedium medium, SourceGroup expected)
    {
        Assert.Equal(expected, DeviceAssociationRules.GetSourceGroup(medium));
    }

    [Fact]
    public void NoMountedSourcesReturnsUnavailable()
    {
        var result = Resolve(GameMedium.Usb);

        Assert.Equal(SourceResolutionStatus.Unavailable, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Null(result.SelectedDevice);
    }

    [Theory]
    [InlineData(GameMedium.Usb, DeviceMedium.MemoryCard)]
    [InlineData(GameMedium.MemoryCard, DeviceMedium.Usb)]
    public void UsbAndCardSourcesAreInterchangeable(GameMedium medium, DeviceMedium label)
    {
        var device = Source("source", DeviceCapabilities.RemovableSource, label);

        var result = Resolve(medium, device);

        Assert.Equal(SourceResolutionStatus.Selected, result.Status);
        Assert.Equal(device.Key, result.SelectedDevice);
        Assert.Equal(device, Assert.Single(result.Candidates));
    }

    [Fact]
    public void OpticalAndRemovableCapabilitiesDoNotCrossGroups()
    {
        var optical = Source("dvd", DeviceCapabilities.OpticalSource, DeviceMedium.Optical);
        var usb = Source("usb");

        Assert.Equal(optical.Key, Resolve(GameMedium.Dvd, optical, usb).SelectedDevice);
        Assert.Equal(usb.Key, Resolve(GameMedium.Usb, optical, usb).SelectedDevice);
        Assert.Equal(SourceResolutionStatus.Unavailable, Resolve(GameMedium.Dvd, usb).Status);
        Assert.Equal(SourceResolutionStatus.Unavailable, Resolve(GameMedium.MemoryCard, optical).Status);
    }

    [Fact]
    public void MultiplePhysicalSourcesRequireAChoiceWithoutSelectingTheFirst()
    {
        var first = Source("first");
        var second = Source("second");

        var result = Resolve(GameMedium.Usb, first, second);

        Assert.Equal(SourceResolutionStatus.ChooseDevice, result.Status);
        Assert.Null(result.SelectedDevice);
        Assert.Equal(new[] { first.Key, second.Key }, result.Candidates.Select(item => item.Key));
    }

    [Fact]
    public void MultipleVolumesAndDuplicateObservationsCountAsOneDevice()
    {
        var device = Source("source");
        device = device with
        {
            Volumes = device.Volumes.Add(device.Volumes[0] with
            {
                Key = new VolumeKey(device.Key, "volume-2"), Root = "/another-root"
            })
        };

        var result = Resolve(GameMedium.Usb, device, device);

        Assert.Equal(SourceResolutionStatus.Selected, result.Status);
        Assert.Single(result.Candidates);
        Assert.Equal(device.Key, result.SelectedDevice);
        Assert.Equal(2, result.Candidates[0].Volumes.Length);
    }

    [Fact]
    public void ProviderScopeDistinguishesIdenticalLocalDeviceIds()
    {
        var first = Source("same-local-id");
        var secondKey = new DeviceKey("another-provider", first.Key.DeviceId);
        var second = first with
        {
            Key = secondKey,
            Volumes = [first.Volumes[0] with { Key = new VolumeKey(secondKey, "volume-1") }]
        };

        Assert.Equal(SourceResolutionStatus.ChooseDevice, Resolve(GameMedium.Usb, first, second).Status);
    }

    [Theory]
    [InlineData(DeviceMedium.Psp, DeviceCapabilities.RemovableSource)]
    [InlineData(DeviceMedium.Usb, DeviceCapabilities.RemovableSource | DeviceCapabilities.PspDestination)]
    public void PspDestinationsAreNeverOrdinarySources(DeviceMedium label, DeviceCapabilities capabilities)
    {
        Assert.Equal(SourceResolutionStatus.Unavailable,
            Resolve(GameMedium.Usb, Source("psp", capabilities, label)).Status);
    }

    [Theory]
    [InlineData(DeviceState.Detecting)]
    [InlineData(DeviceState.Unmounted)]
    [InlineData(DeviceState.Removing)]
    [InlineData(DeviceState.Unavailable)]
    public void UnavailableDevicesCannotUseStaleVolumeRoots(DeviceState state)
    {
        var device = Source("source") with { State = state };

        Assert.Equal(SourceResolutionStatus.Unavailable, Resolve(GameMedium.Usb, device).Status);
    }

    [Theory]
    [InlineData(null, DeviceState.Ready)]
    [InlineData("", DeviceState.Ready)]
    [InlineData("   ", DeviceState.Ready)]
    [InlineData("/old-root", DeviceState.Unmounted)]
    [InlineData("/old-root", DeviceState.Unavailable)]
    [InlineData("/old-root", DeviceState.Removing)]
    [InlineData("/old-root", DeviceState.Mounting)]
    [InlineData("/old-root", DeviceState.Error)]
    public void SourceRequiresAnAvailableMountedVolume(string? root, DeviceState state)
    {
        var device = Source("source");
        device = device with { Volumes = [device.Volumes[0] with { Root = root, State = state }] };

        Assert.Equal(SourceResolutionStatus.Unavailable, Resolve(GameMedium.Usb, device).Status);
    }

    [Fact]
    public void EmptyOrDefaultVolumeInventoryIsNotAMountedSource()
    {
        Assert.Equal(SourceResolutionStatus.Unavailable,
            Resolve(GameMedium.Usb, Source("empty") with { Volumes = [] }).Status);
        Assert.Equal(SourceResolutionStatus.Unavailable,
            Resolve(GameMedium.Usb, Source("default") with { Volumes = default }).Status);
    }

    [Fact]
    public void ErrorOnAnotherVolumeDoesNotHideAnAvailableMountedVolume()
    {
        var device = Source("source");
        device = device with
        {
            State = DeviceState.Error,
            Error = new DeviceOperationError("mount-failed", "Another volume could not be mounted."),
            Volumes = device.Volumes.Add(device.Volumes[0] with
            {
                Key = new VolumeKey(device.Key, "failed-volume"), Root = null, State = DeviceState.Error
            })
        };

        Assert.Equal(device.Key, Resolve(GameMedium.Usb, device).SelectedDevice);
    }

    [Fact]
    public void EligibilityUsesCapabilitiesRatherThanInformativeLabels()
    {
        var capable = Source("capable", DeviceCapabilities.RemovableSource, DeviceMedium.Unknown);
        var labelOnly = Source("label-only", DeviceCapabilities.None, DeviceMedium.MemoryCard);

        Assert.Equal(capable.Key, Resolve(GameMedium.MemoryCard, capable, labelOnly).SelectedDevice);
        Assert.Equal(SourceResolutionStatus.Unavailable, Resolve(GameMedium.Usb, labelOnly).Status);
    }

    [Fact]
    public void VolumesBelongingToAnotherDeviceCannotMakeASourceAvailable()
    {
        var device = Source("source");
        device = device with { Volumes = [Source("other").Volumes[0]] };

        Assert.Equal(SourceResolutionStatus.Unavailable, Resolve(GameMedium.Usb, device).Status);
    }

    [Fact]
    public void DefaultInventoryIsUnavailableAndInvalidGameMediumIsRejected()
    {
        var result = DeviceAssociationRules.ResolveSource(GameMedium.Usb,
            new DeviceInventorySnapshot(1, default));

        Assert.Equal(SourceResolutionStatus.Unavailable, result.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => DeviceAssociationRules.GetSourceGroup((GameMedium)99));
    }

    private static SourceResolution Resolve(GameMedium medium, params DeviceSnapshot[] devices) =>
        DeviceAssociationRules.ResolveSource(medium, new DeviceInventorySnapshot(1, devices.ToImmutableArray()));

    private static DeviceSnapshot Source(string id,
        DeviceCapabilities capabilities = DeviceCapabilities.RemovableSource,
        DeviceMedium medium = DeviceMedium.Usb)
    {
        var key = new DeviceKey("test", id);
        return new DeviceSnapshot(key, "Informative name", medium, capabilities,
            new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly), "media-1", DeviceState.Ready,
            [new VolumeSnapshot(new VolumeKey(key, "volume-1"), null, "/not-probed/" + id,
                new DeviceIdentity(null, DeviceIdentityConfidence.SessionOnly), DeviceState.Ready)]);
    }
}
