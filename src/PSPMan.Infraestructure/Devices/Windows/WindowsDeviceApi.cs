using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Enumerates present storage interfaces using SetupAPI, Configuration Manager and read-only IOCTLs.</summary>
public sealed partial class WindowsDeviceApi : IWindowsDeviceApi
{
    public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows storage enumeration requires Windows.");
        return new ValueTask<WindowsStorageSnapshot>(Task.Run(() => Enumerate(cancellationToken), cancellationToken));
    }

    private static WindowsStorageSnapshot EnumerateCore(CancellationToken cancellationToken)
    {
        var errors = ImmutableArray.CreateBuilder<WindowsDeviceError>();
        var devices = ImmutableArray.CreateBuilder<WindowsStorageDevice>();
        foreach (var (classId, optical) in new[]
        {
            (new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b"), false), // GUID_DEVINTERFACE_DISK
            (new Guid("53f56308-b6bf-11d0-94f2-00a0c91efb8b"), true)   // GUID_DEVINTERFACE_CDROM
        })
        {
            foreach (var item in ReadInterfaces(classId, errors, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var errorStart = errors.Count;
                using var handle = OpenDevice(item.Path, errors);
                var number = handle.IsInvalid ? null : ReadStorageNumber(handle, errors);
                StorageDescriptor? descriptor = handle.IsInvalid ? null : ReadDescriptor(handle, errors);
                var instanceId = ReadInstanceId(item.Node, errors);
                if (instanceId is null)
                    continue; // No invented physical identity after a native lookup failure.
                string? parentId = null;
                var parentResult = Native.CM_Get_Parent(out var parent, item.Node, 0);
                if (parentResult == 0)
                    parentId = ReadInstanceId(parent, errors);
                else
                    errors.Add(ConfigurationError("CM_Get_Parent", parentResult));

                var bus = descriptor?.Bus ?? WindowsStorageBus.Unknown;
                var ancestry = bus == WindowsStorageBus.Usb
                    ? ReadUsbAncestry(item.Node, errors, cancellationToken) : ImmutableArray<WindowsPnpNode>.Empty;
                devices.Add(new WindowsStorageDevice(instanceId, parentId, item.Path,
                    descriptor?.DisplayName ?? item.Name ?? "Removable device", number, bus, optical,
                    IsRemovable: descriptor?.RemovableMedia == true || (item.Capabilities & 0x4) != 0,
                    CanEject: optical || (item.Capabilities & 0x2) != 0,
                    Error: errors.Count > errorStart ? errors[errorStart] : null) { UsbAncestry = ancestry });
            }
        }

        var volumes = ReadVolumes(errors, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsStorageSnapshot(devices.ToImmutable(), volumes, errors.ToImmutable());
    }

    private sealed record StorageInterface(string Path, uint Node, uint Capabilities, string? Name);

    private static List<StorageInterface> ReadInterfaces(Guid classId,
        ImmutableArray<WindowsDeviceError>.Builder errors, CancellationToken cancellationToken)
    {
        var result = new List<StorageInterface>();
        using var set = Native.SetupDiGetClassDevsW(ref classId, null, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (set.IsInvalid)
        {
            errors.Add(LastError("SetupDiGetClassDevs"));
            return result;
        }
        for (uint index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = new Native.InterfaceData { Size = (uint)Marshal.SizeOf<Native.InterfaceData>() };
            if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classId, index, ref data))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != 259) // ERROR_NO_MORE_ITEMS
                    errors.Add(Win32Error("SetupDiEnumDeviceInterfaces", error));
                break;
            }
            var device = new Native.DeviceInfoData { Size = (uint)Marshal.SizeOf<Native.DeviceInfoData>() };
            Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, ref device);
            var sizeError = Marshal.GetLastPInvokeError();
            if (sizeError != 122 || required < 8 || required > 1024 * 1024)
            {
                errors.Add(Win32Error("SetupDiGetDeviceInterfaceDetailSize", sizeError));
                continue;
            }
            var buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                // Native WCHAR path starts at byte 4; cbSize includes platform ABI padding.
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, required, out _, ref device))
                {
                    errors.Add(LastError("SetupDiGetDeviceInterfaceDetail"));
                    continue;
                }
                var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4), ((int)required - 4) / 2)?.TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(path))
                {
                    errors.Add(new WindowsDeviceError("MalformedReply", "ReadDeviceInterfacePath"));
                    continue;
                }
                var capabilities = ReadRegistryProperty(set, ref device, 15); // SPDRP_CAPABILITIES
                var caps = capabilities is { Length: >= 4 } ? BinaryPrimitives.ReadUInt32LittleEndian(capabilities) : 0;
                var friendly = ReadRegistryProperty(set, ref device, 12) // SPDRP_FRIENDLYNAME
                    ?? ReadRegistryProperty(set, ref device, 0); // SPDRP_DEVICEDESC
                result.Add(new StorageInterface(path, device.Node, caps,
                    friendly is null ? null : Encoding.Unicode.GetString(friendly).TrimEnd('\0')));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }

    private static byte[]? ReadRegistryProperty(Native.DeviceInfoSet set, ref Native.DeviceInfoData device, uint property)
    {
        Native.SetupDiGetDeviceRegistryPropertyW(set, ref device, property, out _, null, 0, out var required);
        if (required == 0 || required > 1024 * 1024)
            return null; // Optional display/capability metadata may not be supplied by the driver.
        var bytes = new byte[required];
        return Native.SetupDiGetDeviceRegistryPropertyW(set, ref device, property, out _, bytes, required, out _)
            ? bytes : null;
    }

    private static string? ReadInstanceId(uint node, ImmutableArray<WindowsDeviceError>.Builder errors)
    {
        var status = Native.CM_Get_Device_ID_Size(out var length, node, 0);
        if (status != 0 || length > 1024 * 1024)
        {
            errors.Add(ConfigurationError("CM_Get_Device_ID_Size", status));
            return null;
        }
        var buffer = new StringBuilder((int)length + 1);
        status = Native.CM_Get_Device_IDW(node, buffer, length + 1, 0);
        if (status == 0 && buffer.Length > 0)
            return buffer.ToString();
        errors.Add(ConfigurationError("CM_Get_Device_ID", status));
        return null;
    }

    private static SafeFileHandle OpenDevice(string path, ImmutableArray<WindowsDeviceError>.Builder errors)
    {
        // Zero desired access and full sharing: metadata only, never reads or writes user data.
        var handle = Native.CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            errors.Add(LastError("OpenStorageInterface"));
        return handle;
    }

    private static WindowsStorageNumber? ReadStorageNumber(SafeFileHandle handle,
        ImmutableArray<WindowsDeviceError>.Builder errors)
    {
        var bytes = new byte[12];
        if (!Native.DeviceIoControl(handle, 0x2D1080, null, 0, bytes, 12, out var returned, IntPtr.Zero))
        {
            errors.Add(LastError("IOCTL_STORAGE_GET_DEVICE_NUMBER"));
            return null;
        }
        if (returned < 12)
        {
            errors.Add(new WindowsDeviceError("MalformedReply", "IOCTL_STORAGE_GET_DEVICE_NUMBER"));
            return null;
        }
        return new WindowsStorageNumber(BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
    }

    private static StorageDescriptor? ReadDescriptor(SafeFileHandle handle,
        ImmutableArray<WindowsDeviceError>.Builder errors)
    {
        // STORAGE_PROPERTY_QUERY: StorageDeviceProperty (0), PropertyStandardQuery (0).
        var query = new byte[12];
        var header = new byte[8];
        if (!Native.DeviceIoControl(handle, 0x2D1400, query, 12, header, 8, out var returned, IntPtr.Zero))
        {
            errors.Add(LastError("IOCTL_STORAGE_QUERY_PROPERTY"));
            return null;
        }
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (returned < 8 || size < 36 || size > 1024 * 1024)
        {
            errors.Add(new WindowsDeviceError("MalformedReply", "StorageDescriptorSize"));
            return null;
        }
        var bytes = new byte[size];
        if (!Native.DeviceIoControl(handle, 0x2D1400, query, 12, bytes, size, out returned, IntPtr.Zero))
        {
            errors.Add(LastError("IOCTL_STORAGE_QUERY_PROPERTY"));
            return null;
        }
        try { return ParseDescriptor(bytes.AsSpan(0, checked((int)Math.Min(returned, size)))); }
        catch (InvalidDataException)
        {
            errors.Add(new WindowsDeviceError("MalformedReply", "StorageDescriptor"));
            return null;
        }
    }

    internal sealed record StorageDescriptor(WindowsStorageBus Bus, bool RemovableMedia, string? DisplayName);

    internal static StorageDescriptor ParseDescriptor(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 36)
            throw new InvalidDataException("The storage descriptor is truncated.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (size < 36 || size > bytes.Length)
            throw new InvalidDataException("The storage descriptor size is invalid.");
        bytes = bytes[..(int)size];
        var bus = BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]) switch
        {
            0 => WindowsStorageBus.Unknown,
            7 => WindowsStorageBus.Usb,
            11 => WindowsStorageBus.Sata,
            12 => WindowsStorageBus.Sd,
            13 => WindowsStorageBus.Mmc,
            _ => WindowsStorageBus.Other
        };
        var vendor = ReadDescriptorString(bytes, 12);
        var model = ReadDescriptorString(bytes, 16);
        var name = string.Join(" ", new[] { vendor, model }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new StorageDescriptor(bus, bytes[10] != 0, string.IsNullOrEmpty(name) ? null : name);
    }

    private static string? ReadDescriptorString(ReadOnlySpan<byte> bytes, int offsetPosition)
    {
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offsetPosition..]);
        if (offset == 0)
            return null;
        if (offset < 36 || offset >= bytes.Length)
            throw new InvalidDataException("The storage descriptor string offset is invalid.");
        var content = bytes[(int)offset..];
        var end = content.IndexOf((byte)0);
        if (end < 0)
            throw new InvalidDataException("The storage descriptor string is not terminated.");
        return Encoding.ASCII.GetString(content[..end]).Trim();
    }

    private static WindowsDeviceError LastError(string operation) => Win32Error(operation, Marshal.GetLastPInvokeError());
    private static WindowsDeviceError Win32Error(string operation, int code) => new(
        code switch { 5 => "AccessDenied", 21 or 1112 => "NoMedia", _ => "NativeFailure" }, operation, code,
        new Win32Exception(code).Message);
    private static WindowsDeviceError ConfigurationError(string operation, uint code) =>
        new("ConfigurationManagerFailure", operation, unchecked((int)code));

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct InterfaceData { public uint Size; public Guid ClassGuid; public uint Flags; public nuint Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceInfoData { public uint Size; public Guid ClassGuid; public uint Node; public nuint Reserved; }

        internal sealed class DeviceInfoSet : SafeHandleZeroOrMinusOneIsInvalid
        {
            public DeviceInfoSet() : base(ownsHandle: true) { }
            protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
        }

        [DllImport("setupapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern DeviceInfoSet SetupDiGetClassDevsW(ref Guid classId, string? enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(DeviceInfoSet set, IntPtr device, ref Guid classId, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetailW(DeviceInfoSet set, ref InterfaceData data,
            IntPtr detail, uint detailSize, out uint required, ref DeviceInfoData device);
        [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceRegistryPropertyW(DeviceInfoSet set, ref DeviceInfoData device,
            uint property, out uint type, [Out] byte[]? buffer, uint size, out uint required);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern uint CM_Get_Device_ID_Size(out uint size, uint node, uint flags);
        [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, uint length, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[]? input, uint inputSize,
            [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    }
}
