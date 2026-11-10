using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Configuration Manager interface and target-volume notifications; no device mutations.</summary>
public sealed class WindowsDeviceNotificationApi : IWindowsDeviceNotificationApi
{
    public IDisposable RegisterInterface(Guid classGuid, Action changed)
    {
        EnsureWindows();
        return Register(new Filter { Size = 416, ClassGuid = classGuid }, changed);
    }

    public IDisposable RegisterVolume(string volumeGuidPath, Action changed)
    {
        EnsureWindows();
        // Zero access and full sharing: observe without locking the volume or preventing safe removal.
        using var volume = CreateFileW(volumeGuidPath.TrimEnd('\\'), 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (volume.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Open notification volume");
        var registration = Register(new Filter { Size = 416, Type = 1, Target = volume.DangerousGetHandle() }, changed);
        GC.KeepAlive(volume);
        // CM only needs this file handle during registration; the registration survives its closure.
        return registration;
    }

    private static IDisposable Register(Filter filter, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Callback callback = (_, _, _, _, _) =>
        {
            // Target custom events include mount-name/media changes. Do not inspect unmanaged payloads,
            // enumerate, unregister, or veto a query-remove from this callback.
            try { changed(); }
            catch { /* No managed exception may cross the native callback boundary. */ }
            return 0;
        };
        var result = CM_Register_Notification(ref filter, IntPtr.Zero, callback, out var handle);
        if (result != 0) throw new IOException($"CM_Register_Notification failed (CONFIGRET 0x{result:X8}).");
        return new Registration(handle, callback);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows device notifications require Windows.");
    }

    private sealed class Registration(IntPtr handle, Callback callback) : IDisposable
    {
        private IntPtr _handle = handle;
        // Keep the delegate alive until unregister has waited for every pending callback.
        private readonly Callback _callback = callback;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (current == IntPtr.Zero) return;
            CM_Unregister_Notification(current);
            GC.KeepAlive(_callback);
        }
    }

    // The union also contains WCHAR InstanceId[MAX_DEVICE_ID_LEN=200], even when unused.
    [StructLayout(LayoutKind.Explicit, Size = 416)]
    private struct Filter
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(8)] public uint Type;
        [FieldOffset(16)] public Guid ClassGuid;
        [FieldOffset(16)] public IntPtr Target;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint Callback(IntPtr notification, IntPtr context, uint action, IntPtr data, uint size);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Register_Notification(ref Filter filter, IntPtr context, Callback callback, out IntPtr handle);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Unregister_Notification(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
}
