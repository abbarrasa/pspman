using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace PSPMan.Infraestructure.Devices.Windows;

public sealed partial class WindowsDeviceApi
{
    private static ImmutableArray<WindowsPnpNode> ReadUsbAncestry(uint start,
        ImmutableArray<WindowsDeviceError>.Builder errors, CancellationToken cancellationToken)
    {
        var result = ImmutableArray.CreateBuilder<WindowsPnpNode>();
        var visited = new HashSet<uint>();
        var current = start;
        // Bound malformed/cyclic driver relationships; never traverse other storage branches.
        for (var depth = 0; depth < 64 && visited.Add(current); depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = ReadInstanceId(current, errors);
            if (instance is null)
                return result.ToImmutable();
            var hardware = ReadPnpStrings(current, 2, errors); // CM_DRP_HARDWAREID
            var compatible = ReadPnpStrings(current, 3, errors); // CM_DRP_COMPATIBLEIDS
            var capabilities = ReadPnpProperty(current, 0x10, 4, errors); // CM_DRP_CAPABILITIES, REG_DWORD
            var unique = capabilities.Length == 4 && (BinaryPrimitives.ReadUInt32LittleEndian(capabilities) & 0x10) != 0;
            var node = new WindowsPnpNode(instance, hardware, compatible, unique);
            result.Add(node);
            if (WindowsUsbMetadataReader.IsPhysicalUsbNode(node))
                return result.ToImmutable();
            var status = Native.CM_Get_Parent(out var parent, current, 0);
            if (status != 0)
            {
                errors.Add(ConfigurationError("UsbAncestry.CM_Get_Parent", status));
                return result.ToImmutable();
            }
            current = parent;
        }
        errors.Add(new WindowsDeviceError("InvalidUsbAncestry", "ReadUsbAncestry"));
        return result.ToImmutable();
    }

    private static ImmutableArray<string> ReadPnpStrings(uint node, uint property,
        ImmutableArray<WindowsDeviceError>.Builder errors)
    {
        var bytes = ReadPnpProperty(node, property, 7, errors); // REG_MULTI_SZ
        if (bytes.Length == 0)
            return [];
        if (bytes.Length % 2 != 0 || bytes.Length < 2 || bytes[^1] != 0 || bytes[^2] != 0)
        {
            errors.Add(new WindowsDeviceError("MalformedReply", "ReadUsbPnpIds"));
            return [];
        }
        return Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray();
    }

    private static byte[] ReadPnpProperty(uint node, uint property, uint expectedType,
        ImmutableArray<WindowsDeviceError>.Builder errors)
    {
        uint length = 0;
        var status = Native.CM_Get_DevNode_Registry_PropertyW(node, property, out _, null, ref length, 0);
        if (status == 0x25) // CR_NO_SUCH_VALUE: optional evidence is absent, not a manufactured identity.
            return [];
        if (status != 0x1A && status != 0) // CR_BUFFER_SMALL
        {
            errors.Add(ConfigurationError("CM_Get_DevNode_Registry_Property.Size", status));
            return [];
        }
        if (length == 0)
            return [];
        if (length > 1024 * 1024)
        {
            errors.Add(new WindowsDeviceError("MalformedReply", "UsbPnpPropertySize"));
            return [];
        }
        var buffer = new byte[length];
        status = Native.CM_Get_DevNode_Registry_PropertyW(node, property, out var type, buffer, ref length, 0);
        if (status != 0)
        {
            errors.Add(ConfigurationError("CM_Get_DevNode_Registry_Property", status));
            return [];
        }
        if (type != expectedType || length > buffer.Length)
        {
            errors.Add(new WindowsDeviceError("MalformedReply", "UsbPnpPropertyType"));
            return [];
        }
        return buffer.AsSpan(0, (int)length).ToArray();
    }

    private static partial class Native
    {
        [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property,
            out uint type, [Out] byte[]? buffer, ref uint length, uint flags);
    }
}
