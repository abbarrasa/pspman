using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

internal sealed class WindowsRemovalNativeApi : IWindowsRemovalNativeApi
{
    public IWindowsRemovalVolume? OpenVolume(WindowsVolumeReference volume, out WindowsDeviceError? error)
    {
        // Read access permits read-only sources; no user-file writes or destructive filesystem controls.
        var handle = Native.CreateFileW(volume.VolumeGuidPath!.TrimEnd('\\'), 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        error = handle.IsInvalid ? LastError("OpenRemovalVolume") : null;
        if (error is null) return new Volume(handle);
        handle.Dispose();
        return null;
    }

    public WindowsDeviceRemovalFailure? LocateDevice(string instanceId, out uint node)
    {
        // NORMAL only: never locate phantom nodes, cancel removal, or default to the device-tree root.
        var status = Native.CM_Locate_DevNodeW(out node, instanceId, 0);
        return status == 0 ? null : ConfigurationFailure("CM_Locate_DevNode", status);
    }

    public WindowsDeviceRemovalFailure? RequestEject(uint node)
    {
        var name = new StringBuilder(260); // MAX_PATH required by Configuration Manager.
        var status = Native.CM_Request_Device_EjectW(node, out var veto, name, (uint)name.Capacity, 0);
        return status == 0 ? null : ConfigurationFailure("CM_Request_Device_Eject", status) with
        { VetoType = veto, VetoName = name.ToString() };
    }

    private static WindowsDeviceRemovalFailure ConfigurationFailure(string operation, uint status) =>
        new(new(status == 0x17 ? "RemovalVetoed" : "ConfigurationManagerFailure", operation,
            unchecked((int)status), $"Configuration Manager returned 0x{status:X8}.")) { ConfigurationStatus = status };

    private static WindowsDeviceError LastError(string operation)
    {
        var code = Marshal.GetLastPInvokeError();
        var category = operation == "FSCTL_LOCK_VOLUME" && code is 5 or 32 or 33 ? "VolumeBusy"
            : code switch { 5 => "AccessDenied", 1314 => "PrivilegeNotHeld", 21 or 1112 => "NoMedia", _ => "NativeFailure" };
        return new(category, operation, code, new Win32Exception(code).Message);
    }

    private sealed class Volume(SafeFileHandle handle) : IWindowsRemovalVolume
    {
        public WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error)
        {
            var bytes = new byte[12];
            if (!Native.ReadDeviceControl(handle, 0x2D1080, IntPtr.Zero, 0, bytes, 12, out var returned, IntPtr.Zero))
            {
                error = LastError("IOCTL_STORAGE_GET_DEVICE_NUMBER");
                return null;
            }
            if (returned != 12)
            {
                error = new("MalformedReply", "IOCTL_STORAGE_GET_DEVICE_NUMBER");
                return null;
            }
            error = null;
            return new(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        }

        public WindowsDeviceError? Lock() => Native.ControlVolume(handle, 0x90018, IntPtr.Zero, 0,
            IntPtr.Zero, 0, out _, IntPtr.Zero) ? null : LastError("FSCTL_LOCK_VOLUME");

        // FSCTL_LOCK_VOLUME flushes cached data. Closing the handle releases its lock even on failure/cancellation.
        public void Dispose() => handle.Dispose();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadDeviceControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
            [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ControlVolume(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
            IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
        [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern uint CM_Locate_DevNodeW(out uint node, string instanceId, uint flags);
        [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern uint CM_Request_Device_EjectW(uint node, out uint vetoType, StringBuilder vetoName,
            uint nameLength, uint flags);
    }
}
