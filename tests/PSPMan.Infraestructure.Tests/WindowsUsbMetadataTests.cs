using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsUsbMetadataTests
{
    [Theory]
    [InlineData("01C8")]
    [InlineData("02D2")]
    public async Task RecognizesPspUsingPnPDescriptorsAndStorageEvidence(string product)
    {
        var device = Disk("psp", "SERIAL-1", product: product);
        var provider = new WindowsDeviceProvider(new FakeApi([device], [Volume()]));

        var observed = Assert.Single((await provider.GetInventoryAsync()).Devices);

        Assert.True(observed.IsPspDestination);
        Assert.False(observed.IsSourceEligible);
        Assert.Equal(PspUsbRecognitionStatus.Recognized, observed.PspRecognition?.Status);
        Assert.Equal((ushort)0x054c, observed.UsbMetadata?.VendorId);
        Assert.Equal("SERIAL-1", observed.UsbMetadata?.Serial);
        Assert.Equal(DeviceIdentityConfidence.Persistent, observed.Identity.Confidence);
        Assert.Equal(DeviceIdentityConfidence.SessionOnly, observed.MediaIdentity.Confidence);
        Assert.Null(observed.MediaIdentity.PersistentId);
    }

    [Theory]
    [InlineData("1234", "5678", true)]
    [InlineData("054C", "01C9", true)]
    [InlineData("054C", "9999", true)]
    [InlineData("054C", "01C8", false)]
    public async Task DoesNotRecognizeGenericStorageDevelopmentProtocolOrMissingStorage(string vendor, string product, bool available)
    {
        var device = Disk("generic", "SERIAL", vendor, product) with { DisplayName = "PSP/GAME" };
        var volume = Volume() with { Label = "PSP", State = available ? WindowsVolumeState.Available : WindowsVolumeState.NoMedia };

        var observed = Assert.Single((await new WindowsDeviceProvider(new FakeApi([device], [volume])).GetInventoryAsync()).Devices);

        Assert.False(observed.IsPspDestination);
        Assert.True(observed.IsSourceEligible);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("6&generated&0&2", true)]
    [InlineData("looks-like-a-serial", false)]
    public async Task MissingOrLocationGeneratedSerialDoesNotEnablePersistentIdentity(string? serial, bool unique)
    {
        var device = Disk("reader", serial) with { UsbAncestry = Ancestry(serial, unique: unique) };

        var observed = Assert.Single((await new WindowsDeviceProvider(new FakeApi([device], [Volume()])).GetInventoryAsync()).Devices);

        Assert.Equal(DeviceIdentityConfidence.SessionOnly, observed.Identity.Confidence);
        Assert.Null(observed.Identity.PersistentId);
    }

    [Fact]
    public async Task DuplicateSerialsNeverEnableAutomaticIdentityRecovery()
    {
        var api = new FakeApi([Disk("first", "DUPLICATE"), Disk("second", "DUPLICATE")], [Volume()]);
        var provider = new WindowsDeviceProvider(api);
        var inventory = await provider.GetInventoryAsync();

        Assert.Equal(2, inventory.Devices.Length);
        Assert.All(inventory.Devices, device =>
        {
            Assert.Equal(DeviceIdentityConfidence.SessionOnly, device.Identity.Confidence);
            Assert.Null(device.Identity.PersistentId);
        });
        api.Devices = [Disk("second", "DUPLICATE")];
        var remaining = Assert.Single((await provider.GetInventoryAsync()).Devices);
        Assert.Equal(DeviceIdentityConfidence.SessionOnly, remaining.Identity.Confidence);
    }

    [Fact]
    public async Task ChangedCardsAndDisconnectionChangeGenerationButRootsAndLabelsDoNot()
    {
        var api = new FakeApi([Disk("reader", "READER")], [Volume()]);
        var provider = new WindowsDeviceProvider(api);
        var first = Assert.Single((await provider.GetInventoryAsync()).Devices);
        api.Volumes = [Volume() with { MountPoints = [@"G:\"], Label = "Renamed" }];
        var moved = Assert.Single((await provider.GetInventoryAsync()).Devices);
        Assert.Equal(first.MediaGeneration, moved.MediaGeneration);
        Assert.Equal(first.Identity, moved.Identity);
        api.Volumes = [Volume() with { VolumeGuidPath = @"\\?\Volume{22222222-2222-2222-2222-222222222222}\" }];
        var changed = Assert.Single((await provider.GetInventoryAsync()).Devices);
        Assert.NotEqual(first.MediaGeneration, changed.MediaGeneration);
        Assert.Equal(DeviceIdentityConfidence.SessionOnly, changed.MediaIdentity.Confidence);
        api.Devices = [];
        await provider.GetInventoryAsync();
        api.Devices = [Disk("reader", "READER")];
        var reconnected = Assert.Single((await provider.GetInventoryAsync()).Devices);
        Assert.NotEqual(changed.MediaGeneration, reconnected.MediaGeneration);
        Assert.Equal(DeviceIdentityConfidence.SessionOnly, reconnected.MediaIdentity.Confidence);
    }

    [Fact]
    public void DoesNotBorrowAnUnrelatedHubIdentityOrMassStorageInterface()
    {
        var own = new WindowsPnpNode(@"USB\VID_1234&PID_5678\OWN", [@"USB\VID_1234&PID_5678"], [], true);
        var hub = new WindowsPnpNode(@"USB\VID_054C&PID_01C8\HUB", [@"USB\VID_054C&PID_01C8"],
            [@"USB\Class_08&SubClass_06&Prot_50"], true);

        var metadata = WindowsUsbMetadataReader.Read([own, hub]);

        Assert.NotNull(metadata);
        Assert.Equal((ushort)0x1234, metadata.VendorId);
        Assert.Empty(metadata.Interfaces);
    }

    [Fact]
    public void CompositeInterfaceEvidenceBelongsToItsPhysicalUsbParent()
    {
        var function = new WindowsPnpNode(@"USB\VID_054C&PID_01C8&MI_00\generated",
            [@"USB\VID_054C&PID_01C8&MI_00"], [@"USB\Class_08&SubClass_06&Prot_50"], false);
        var parent = new WindowsPnpNode(@"USB\VID_054C&PID_01C8\SERIAL", [@"USB\VID_054C&PID_01C8"], [], true);

        var metadata = WindowsUsbMetadataReader.Read([function, parent]);

        Assert.NotNull(metadata);
        Assert.Equal("SERIAL", metadata.Serial);
        Assert.Equal(new UsbInterfaceDescriptor(8, 6, 0x50), Assert.Single(metadata.Interfaces));
        Assert.Null(WindowsUsbMetadataReader.Read([function]));
    }

    [Fact]
    public async Task SonyIdentifiersWithoutMassStorageInterfaceEvidenceRemainInconclusive()
    {
        var device = Disk("psp", "SERIAL") with
        {
            UsbAncestry = [new WindowsPnpNode(@"USB\VID_054C&PID_01C8\SERIAL",
                [@"USB\VID_054C&PID_01C8"], [@"USB\Class_03&SubClass_01&Prot_01"], true)]
        };

        var observed = Assert.Single((await new WindowsDeviceProvider(new FakeApi([device], [Volume()])).GetInventoryAsync()).Devices);

        Assert.False(observed.IsPspDestination);
        Assert.Equal("MassStorageInterfaceMissing", observed.PspRecognition?.ReasonCode);
    }

    [Fact]
    public async Task ReformattingAtTheSameRootAndGuidInvalidatesTheMediaGeneration()
    {
        var api = new FakeApi([Disk("reader", "READER")], [Volume()]);
        var provider = new WindowsDeviceProvider(api);
        var first = Assert.Single((await provider.GetInventoryAsync()).Devices);
        api.Volumes = [Volume() with { FileSystemSerialNumber = 5678 }];

        var changed = Assert.Single((await provider.GetInventoryAsync()).Devices);

        Assert.NotEqual(first.MediaGeneration, changed.MediaGeneration);
        Assert.Equal(first.Identity, changed.Identity);
        Assert.Null(changed.MediaIdentity.PersistentId);
    }

    [Fact]
    public void ConflictingHardwareAndInstanceDescriptorsDoNotSupplyIdentity()
    {
        var node = new WindowsPnpNode(@"USB\VID_1234&PID_5678\SERIAL",
            [@"USB\VID_054C&PID_01C8"], [@"USB\Class_08&SubClass_06&Prot_50"], true);

        Assert.Null(WindowsUsbMetadataReader.Read([node]));
    }

    private static WindowsStorageDevice Disk(string id, string? serial, string vendor = "054C", string product = "01C8") =>
        new(id, "parent-" + id, "interface-" + id, id, new WindowsStorageNumber(7, id == "second" ? 2u : 1u),
            WindowsStorageBus.Usb, false, true, true) { UsbAncestry = Ancestry(serial, vendor, product) };

    private static ImmutableArray<WindowsPnpNode> Ancestry(string? serial, string vendor = "054C", string product = "01C8", bool unique = true) =>
        [new WindowsPnpNode("USB\\VID_" + vendor + "&PID_" + product + "\\" + (serial ?? "6&generated&0&1"),
            ["USB\\VID_" + vendor + "&PID_" + product], [@"USB\Class_08&SubClass_06&Prot_50"], unique && serial is not null)];

    private static WindowsVolumeReference Volume() => new("volume", new WindowsStorageNumber(7, 1))
    {
        VolumeGuidPath = @"\\?\Volume{11111111-1111-1111-1111-111111111111}\",
        State = WindowsVolumeState.Available, MountPoints = [@"E:\"], FileSystemSerialNumber = 1234
    };

    private sealed class FakeApi(ImmutableArray<WindowsStorageDevice> devices, ImmutableArray<WindowsVolumeReference> volumes) : IWindowsDeviceApi
    {
        public ImmutableArray<WindowsStorageDevice> Devices { get; set; } = devices;
        public ImmutableArray<WindowsVolumeReference> Volumes { get; set; } = volumes;
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WindowsStorageSnapshot(Devices, Volumes, []));
    }
}
