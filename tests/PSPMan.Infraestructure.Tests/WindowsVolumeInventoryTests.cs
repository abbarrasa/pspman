using System.Collections.Immutable;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsVolumeInventoryTests
{
    private const string FirstGuid = @"\\?\Volume{11111111-1111-1111-1111-111111111111}\";
    private const string SecondGuid = @"\\?\Volume{22222222-2222-2222-2222-222222222222}\";

    [Fact]
    public async Task PreservesGuidIdentityDriveAndMountedFolderRootsUnderTheCorrectParent()
    {
        var first = Volume(FirstGuid, 1, "Games", @"E:\", @"C:\Mounted Games\");
        var second = Volume(SecondGuid, 2, "Games", @"F:\");
        var provider = Provider([first, second]);

        var inventory = await provider.GetInventoryAsync();

        var mapped = Assert.Single(inventory.Devices.Single(device => device.InstanceId == "disk-1").Volumes);
        Assert.Equal(FirstGuid, mapped.VolumeGuidPath);
        Assert.Equal("Games", mapped.Label);
        Assert.Equal(new[] { @"E:\", @"C:\Mounted Games\" }, mapped.MountPoints);
        Assert.Equal(WindowsVolumeState.Available, mapped.State);
        Assert.True(mapped.IsMounted);
        Assert.Equal(SecondGuid, Assert.Single(inventory.Devices.Single(device => device.InstanceId == "disk-2").Volumes).VolumeGuidPath);
    }

    [Fact]
    public async Task RetainsMissingAndDuplicateLabelsWithoutCollapsingDistinctVolumes()
    {
        var volumes = new[]
        {
            Volume(FirstGuid, 1, "", @"E:\"),
            Volume(SecondGuid, 1, "", @"F:\")
        };

        var inventory = await Provider(volumes).GetInventoryAsync();

        var mapped = inventory.Devices.Single(device => device.InstanceId == "disk-1").Volumes;
        Assert.Equal(2, mapped.Length);
        Assert.All(mapped, volume => Assert.Equal("", volume.Label));
        Assert.NotEqual(mapped[0].VolumeGuidPath, mapped[1].VolumeGuidPath);
    }

    [Fact]
    public async Task RefreshReplacesRootsAndDoesNotUseAReusedLetterAsIdentity()
    {
        var api = new FakeApi([Volume(FirstGuid, 1, null, @"E:\")]);
        var provider = new WindowsDeviceProvider(api);
        var initial = await provider.GetInventoryAsync();
        api.Volumes = [Volume(FirstGuid, 1, null, @"G:\"), Volume(SecondGuid, 1, null, @"E:\")];

        var updated = await provider.GetInventoryAsync();

        Assert.Equal(@"E:\", Assert.Single(initial.Devices[0].Volumes[0].MountPoints));
        var volumes = updated.Devices[0].Volumes;
        Assert.Equal(@"G:\", Assert.Single(volumes.Single(volume => volume.VolumeGuidPath == FirstGuid).MountPoints));
        Assert.Equal(SecondGuid, volumes.Single(volume => volume.MountPoints.Contains(@"E:\")).VolumeGuidPath);
        Assert.All(volumes, volume => Assert.Null(volume.Label));
    }

    [Theory]
    [InlineData(WindowsVolumeState.NoMedia, "NoMedia", 21)]
    [InlineData(WindowsVolumeState.Unavailable, "AccessDenied", 5)]
    public async Task PreservesErrorsWithoutAdvertisingStaleRoots(WindowsVolumeState state, string code, int nativeCode)
    {
        var error = new WindowsDeviceError(code, "GetVolumeInformation", nativeCode);
        var volume = Volume(FirstGuid, 1, null, @"E:\") with { State = state, Error = error };

        var inventory = await Provider([volume]).GetInventoryAsync();

        var mapped = Assert.Single(inventory.Devices[0].Volumes);
        Assert.Equal(state, mapped.State);
        Assert.Same(error, mapped.Error);
        Assert.Empty(mapped.MountPoints);
        Assert.False(mapped.IsMounted);
    }

    [Fact]
    public async Task KeepsAccessibleVolumesWithoutMountPointsUnmountedAndDoesNotInventALetter()
    {
        var inventory = await Provider([Volume(FirstGuid, 1, "Games")]).GetInventoryAsync();

        var volume = Assert.Single(inventory.Devices[0].Volumes);
        Assert.Equal(WindowsVolumeState.Available, volume.State);
        Assert.Empty(volume.MountPoints);
        Assert.False(volume.IsMounted);
        Assert.Null(volume.Error);
    }

    [Fact]
    public async Task DeduplicatesByGuidRatherThanAnInterfaceAlias()
    {
        var volume = Volume(FirstGuid, 1, "Games", @"E:\");
        var alias = volume with { InterfacePath = "another-interface-alias" };

        var inventory = await Provider([volume, alias]).GetInventoryAsync();

        Assert.Single(inventory.Devices[0].Volumes);
    }

    [Fact]
    public void ParsesCompleteNativeMountPointListsWithoutChangingPathText()
    {
        var roots = WindowsDeviceApi.ParseMountPoints("E:\\\0C:\\Mounted Games\\\0\0".AsSpan());

        Assert.Equal(new[] { @"E:\", @"C:\Mounted Games\" }, roots);
        Assert.Empty(WindowsDeviceApi.ParseMountPoints("\0\0".AsSpan()));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceApi.ParseMountPoints("E:\\\0".AsSpan()));
    }

    [Theory]
    [InlineData(FirstGuid, true)]
    [InlineData(SecondGuid, true)]
    [InlineData(@"E:\", false)]
    [InlineData(@"C:\Mounted Games\", false)]
    [InlineData(@"\\?\Volume{not-a-guid}\", false)]
    public void AcceptsOnlyCanonicalVolumeGuidPathsNotRootsOrLabels(string path, bool expected)
    {
        Assert.Equal(expected, WindowsDeviceApi.IsVolumeGuidPath(path));
    }

    private static WindowsVolumeReference Volume(string guid, uint number, string? label, params string[] roots) =>
        new(guid.TrimEnd('\\'), new WindowsStorageNumber(7, number))
        {
            VolumeGuidPath = guid,
            Label = label,
            MountPoints = roots.ToImmutableArray(),
            State = WindowsVolumeState.Available
        };

    private static WindowsDeviceProvider Provider(IEnumerable<WindowsVolumeReference> volumes) => new(new FakeApi(volumes.ToImmutableArray()));

    private sealed class FakeApi(ImmutableArray<WindowsVolumeReference> volumes) : IWindowsDeviceApi
    {
        public ImmutableArray<WindowsVolumeReference> Volumes { get; set; } = volumes;

        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WindowsStorageSnapshot([Disk(1), Disk(2)], Volumes, []));

        private static WindowsStorageDevice Disk(uint number) => new("disk-" + number, "parent-" + number,
            "disk-interface-" + number, "USB storage", new WindowsStorageNumber(7, number),
            WindowsStorageBus.Usb, false, true, true);
    }
}
