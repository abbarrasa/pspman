using System.Buffers.Binary;
using System.Text;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceApiTests
{
    [Theory]
    [InlineData(7, WindowsStorageBus.Usb)]
    [InlineData(11, WindowsStorageBus.Sata)]
    [InlineData(12, WindowsStorageBus.Sd)]
    [InlineData(13, WindowsStorageBus.Mmc)]
    [InlineData(0, WindowsStorageBus.Unknown)]
    [InlineData(17, WindowsStorageBus.Other)]
    public void ReadsNativeDescriptorWithoutUsingDevicePathOrDisplayNameAsIdentity(uint bus, WindowsStorageBus expected)
    {
        var bytes = Descriptor(bus);

        var descriptor = WindowsDeviceApi.ParseDescriptor(bytes);

        Assert.Equal(expected, descriptor.Bus);
        Assert.True(descriptor.RemovableMedia);
        Assert.Equal("Vendor Reader", descriptor.DisplayName);
    }

    [Fact]
    public void AllowsMissingDisplayStringsWithoutFabricatingThem()
    {
        var bytes = Descriptor(7);
        bytes[10] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0);

        var descriptor = WindowsDeviceApi.ParseDescriptor(bytes);

        Assert.Null(descriptor.DisplayName);
        Assert.False(descriptor.RemovableMedia);
    }

    [Fact]
    public void RejectsTruncatedDescriptorsAndOutOfBoundsOrUnterminatedStrings()
    {
        Assert.Throws<InvalidDataException>(() => WindowsDeviceApi.ParseDescriptor(new byte[8]));
        var bytes = Descriptor(7);
        Assert.Throws<InvalidDataException>(() => WindowsDeviceApi.ParseDescriptor(bytes.AsSpan(0, 36)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => WindowsDeviceApi.ParseDescriptor(bytes));
        bytes = Descriptor(7);
        bytes[^1] = 65;
        Assert.Throws<InvalidDataException>(() => WindowsDeviceApi.ParseDescriptor(bytes));
    }

    private static byte[] Descriptor(uint bus)
    {
        var names = Encoding.ASCII.GetBytes("Vendor\0Reader\0");
        var bytes = new byte[36 + names.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        bytes[10] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 43);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), bus);
        names.CopyTo(bytes, 36);
        return bytes;
    }
}
