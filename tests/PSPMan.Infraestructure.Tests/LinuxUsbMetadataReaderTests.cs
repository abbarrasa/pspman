using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxUsbMetadataReaderTests
{
    [Fact]
    public void ReadsUsbIdentityAndMassStorageInterfaceFromSysfsAncestry()
    {
        using var fixture = SysfsFixture.Create("sdb1", serial: "PSP-SERIAL-01");
        var reader = new LinuxUsbMetadataReader(fixture.RootPath);

        var metadata = reader.Read("/dev/sdb1");

        Assert.Equal((ushort)0x054c, metadata.VendorId);
        Assert.Equal((ushort)0x01c8, metadata.ProductId);
        Assert.Equal("PSP-SERIAL-01", metadata.Serial);
        Assert.Contains(metadata.Interfaces, item =>
            item.ClassCode == 0x08 && item.SubclassCode == 0x06 && item.ProtocolCode == 0x50);
        Assert.Equal(LinuxUsbIdentityConfidence.Reliable, metadata.IdentityConfidence);

        var recognition = PspUsbRecognition.Recognize(new PspUsbDescriptor(
            metadata.VendorId!.Value,
            metadata.ProductId!.Value,
            HasAccessibleStorage: true,
            Interfaces: metadata.Interfaces.Select(item => new UsbInterfaceDescriptor(
                item.ClassCode, item.SubclassCode, item.ProtocolCode)).ToArray()));

        Assert.Equal(PspUsbRecognitionStatus.Recognized, recognition.Status);
    }

    [Fact]
    public void TreatsMissingSerialAsAmbiguousAndDoesNotUseDirectoryHints()
    {
        using var fixture = SysfsFixture.Create("sdb1", serial: null, directoryHint: "PSP/GAME");
        var reader = new LinuxUsbMetadataReader(fixture.RootPath);

        var metadata = reader.Read("/dev/sdb1");

        Assert.Equal(LinuxUsbIdentityConfidence.Ambiguous, metadata.IdentityConfidence);
        Assert.Null(metadata.Serial);
        Assert.Null(metadata.ProductLabel);
        Assert.Null(metadata.VolumeLabel);
    }

    [Fact]
    public void SeparatesObservedMediaGenerationsWithoutTreatingReaderSerialAsCardIdentity()
    {
        using var fixture = SysfsFixture.Create("sdb1", serial: "READER-01");
        fixture.AddBlock("sdc1");
        var reader = new LinuxUsbMetadataReader(fixture.RootPath);

        var first = reader.Read("/dev/sdb1");
        var second = reader.Read("/dev/sdc1");

        Assert.Equal(first.Serial, second.Serial);
        Assert.NotEqual(first.MediaGeneration, second.MediaGeneration);
        Assert.Equal(LinuxUsbIdentityConfidence.Ambiguous, first.MediaIdentityConfidence);
        Assert.Equal(LinuxUsbIdentityConfidence.Ambiguous, second.MediaIdentityConfidence);
    }

    private sealed class SysfsFixture : IDisposable
    {
        private SysfsFixture(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static SysfsFixture Create(string blockName, string? serial, string? directoryHint = null)
        {
            var fixture = new SysfsFixture(Directory.CreateTempSubdirectory("pspman-sysfs-").FullName);
            fixture.AddBlock(blockName, serial, directoryHint);
            return fixture;
        }

        public void AddBlock(string blockName, string? serial = "READER-01", string? directoryHint = null)
        {
            var device = Path.Combine(RootPath, "class", "block", blockName, "device", "1-2", "1-2:1.0");
            Directory.CreateDirectory(device);
            Write(Path.Combine(device, "..", "idVendor"), "054c");
            Write(Path.Combine(device, "..", "idProduct"), "01c8");
            if (serial is not null)
                Write(Path.Combine(device, "..", "serial"), serial);
            Write(Path.Combine(device, "bInterfaceClass"), "08");
            Write(Path.Combine(device, "bInterfaceSubClass"), "06");
            Write(Path.Combine(device, "bInterfaceProtocol"), "50");
            if (directoryHint is not null)
                Write(Path.Combine(RootPath, "class", "block", blockName, directoryHint), "ignored");
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);

        private static void Write(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
    }
}
