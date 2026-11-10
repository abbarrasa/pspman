using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Optical media controls only. No PnP removal, forced dismount or media-lock override.</summary>
internal sealed class WindowsOpticalNativeApi : IWindowsOpticalNativeApi
{
    public IWindowsOpticalHandle? Open(string path, out WindowsDeviceError? error)
    {
        if (!Native.SetThreadErrorMode(Native.GetThreadErrorMode() | 1, out var previous))
        {
            error = LastError("SetThreadErrorMode");
            return null;
        }
        try
        {
            var handle = Native.CreateFileW(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            error = handle.IsInvalid ? LastError("OpenOpticalDevice") : null;
            if (error is null) return new Handle(handle);
            handle.Dispose();
            return null;
        }
        finally { Native.SetThreadErrorMode(previous, out _); }
    }

    private sealed class Handle(SafeFileHandle handle) : IWindowsOpticalHandle
    {
        public WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error)
        {
            var bytes = new byte[12];
            error = Control(handle, 0x2D1080, "IOCTL_STORAGE_GET_DEVICE_NUMBER", bytes, out var returned);
            if (error is not null) return null;
            if (returned != 12)
            {
                error = new("MalformedReply", "IOCTL_STORAGE_GET_DEVICE_NUMBER");
                return null;
            }
            return new(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        }

        public WindowsOpticalMediaObservation CheckMedia()
        {
            var bytes = new byte[4];
            var error = Control(handle, 0x2D0800, "IOCTL_STORAGE_CHECK_VERIFY2", bytes, out var returned);
            if (error?.Code == "NoMedia") return new(false);
            if (error is not null) return new(false, Error: error);
            if (returned is not (0 or 4)) return new(false, Error: new("MalformedReply", "IOCTL_STORAGE_CHECK_VERIFY2"));
            return new(true, returned == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : null);
        }

        public WindowsDeviceError? Lock() => Control(handle, 0x90018, "FSCTL_LOCK_VOLUME", null, out _);
        public WindowsDeviceError? Eject() => Control(handle, 0x2D4808, "IOCTL_STORAGE_EJECT_MEDIA", null, out _);
        public void Dispose() => handle.Dispose();
    }

    private static WindowsDeviceError? Control(SafeFileHandle handle, uint code, string operation, byte[]? output, out uint returned)
    {
        returned = 0;
        // Empty trays must not open critical-error dialogs. Restore the exact mode on this worker thread.
        if (!Native.SetThreadErrorMode(Native.GetThreadErrorMode() | 1, out var previous))
            return LastError("SetThreadErrorMode");
        try
        {
            return Native.DeviceIoControl(handle, code, IntPtr.Zero, 0, output, (uint)(output?.Length ?? 0), out returned, IntPtr.Zero)
                ? null : LastError(operation);
        }
        finally { Native.SetThreadErrorMode(previous, out _); }
    }

    private static WindowsDeviceError LastError(string operation)
    {
        var code = Marshal.GetLastPInvokeError();
        var category = operation == "FSCTL_LOCK_VOLUME" && code is 5 or 32 or 33 ? "VolumeBusy" : code switch
        {
            1 or 50 => "OperationUnsupported",
            5 => "AccessDenied",
            1314 => "PrivilegeNotHeld",
            21 or 1112 => "NoMedia",
            32 or 33 or 170 or 2404 => "VolumeBusy",
            1110 => "MediaChanged",
            _ => "NativeFailure"
        };
        return new(category, operation, code, new Win32Exception(code).Message);
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
            [Out] byte[]? output, uint outputSize, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetThreadErrorMode(uint mode, out uint previousMode);
        [DllImport("kernel32.dll")]
        internal static extern uint GetThreadErrorMode();
    }
}
