using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

public sealed partial class WindowsDeviceApi
{
    private static WindowsStorageSnapshot Enumerate(CancellationToken cancellationToken)
    {
        // Suppress empty-reader critical-error dialogs only on this worker thread, restoring its prior mode.
        if (!Native.SetThreadErrorMode(Native.GetThreadErrorMode() | 1, out var previousMode))
            return new WindowsStorageSnapshot([], [], [LastError("SetThreadErrorMode")]);
        try { return EnumerateCore(cancellationToken); }
        finally { Native.SetThreadErrorMode(previousMode, out _); }
    }

    private static ImmutableArray<WindowsVolumeReference> ReadVolumes(
        ImmutableArray<WindowsDeviceError>.Builder errors, CancellationToken cancellationToken)
    {
        var volumes = ImmutableArray.CreateBuilder<WindowsVolumeReference>();
        var name = new StringBuilder(64); // A canonical volume GUID path is 49 UTF-16 code units.
        using var search = Native.FindFirstVolumeW(name, (uint)name.Capacity);
        if (search.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            if (code != 18) // ERROR_NO_MORE_FILES, not a fabricated successful scan after another failure.
                errors.Add(Win32Error("FindFirstVolume", code));
            return [];
        }
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guidPath = name.ToString();
            if (!IsVolumeGuidPath(guidPath))
                errors.Add(new WindowsDeviceError("MalformedReply", "ReadVolumeGuidPath"));
            else
                volumes.Add(ReadVolume(guidPath, errors, cancellationToken));
            name.Clear();
            cancellationToken.ThrowIfCancellationRequested();
        } while (Native.FindNextVolumeW(search, name, (uint)name.Capacity));

        var lastError = Marshal.GetLastPInvokeError();
        if (lastError != 18)
            errors.Add(Win32Error("FindNextVolume", lastError));
        return volumes.ToImmutable();
    }

    private static WindowsVolumeReference ReadVolume(string guidPath,
        ImmutableArray<WindowsDeviceError>.Builder errors, CancellationToken cancellationToken)
    {
        var errorStart = errors.Count;
        // Volume GUID roots require the trailing slash for metadata, but CreateFile requires it removed.
        var devicePath = guidPath.TrimEnd('\\');
        using var handle = OpenDevice(devicePath, errors);
        var number = handle.IsInvalid ? null : ReadStorageNumber(handle, errors);
        cancellationToken.ThrowIfCancellationRequested();
        var mountPoints = ReadMountPoints(guidPath, errors, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var label = new StringBuilder(261);
        var available = Native.GetVolumeInformationW(guidPath, label, (uint)label.Capacity,
            out var serial, out _, out _, IntPtr.Zero, 0);
        WindowsDeviceError? mediaError = null;
        if (!available)
        {
            mediaError = LastError("GetVolumeInformation");
            errors.Add(mediaError);
        }
        var state = mediaError?.Code == "NoMedia" ? WindowsVolumeState.NoMedia
            : available && errors.Count == errorStart ? WindowsVolumeState.Available : WindowsVolumeState.Unavailable;
        return new WindowsVolumeReference(devicePath, number)
        {
            VolumeGuidPath = guidPath,
            Label = available ? label.ToString() : null,
            FileSystemSerialNumber = available ? serial : null,
            MountPoints = state == WindowsVolumeState.Available ? mountPoints : [],
            State = state,
            Error = errors.Count > errorStart ? errors[errorStart] : null
        };
    }

    private static ImmutableArray<string> ReadMountPoints(string guidPath,
        ImmutableArray<WindowsDeviceError>.Builder errors, CancellationToken cancellationToken)
    {
        uint capacity = 256;
        // Retry only a growing buffer, with a bound against continual mount-point changes or malformed sizes.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = new char[capacity];
            if (Native.GetVolumePathNamesForVolumeNameW(guidPath, buffer, capacity, out var length))
            {
                try
                {
                    if (length > capacity)
                        throw new InvalidDataException("The native mount-point list length exceeds its buffer.");
                    // Some drivers represent an empty list with one NUL; nonempty lists require two.
                    return ParseMountPoints(buffer.AsSpan(0, (int)length));
                }
                catch (InvalidDataException)
                {
                    errors.Add(new WindowsDeviceError("MalformedReply", "GetVolumePathNamesForVolumeName"));
                    return [];
                }
            }
            var code = Marshal.GetLastPInvokeError();
            if (code != 234) // ERROR_MORE_DATA
            {
                errors.Add(Win32Error("GetVolumePathNamesForVolumeName", code));
                return [];
            }
            if (length <= capacity || length > 1024 * 1024)
            {
                errors.Add(new WindowsDeviceError("MalformedReply", "VolumeMountPointBufferSize"));
                return [];
            }
            capacity = length;
        }
        errors.Add(new WindowsDeviceError("MountPointsChanged", "GetVolumePathNamesForVolumeName"));
        return [];
    }

    internal static bool IsVolumeGuidPath(string path) => path.Length == 49
        && path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(@"}\", StringComparison.Ordinal)
        && Guid.TryParseExact(path.AsSpan(11, 36), "D", out _);

    internal static ImmutableArray<string> ParseMountPoints(ReadOnlySpan<char> buffer)
    {
        if (buffer.Length == 1 && buffer[0] == '\0')
            return [];
        if (buffer.Length < 2 || buffer[^1] != '\0' || buffer[^2] != '\0')
            throw new InvalidDataException("The native mount-point list is not terminated.");
        var result = ImmutableArray.CreateBuilder<string>();
        var position = 0;
        while (position < buffer.Length - 1)
        {
            var end = buffer[position..].IndexOf('\0');
            if (end == 0)
                break;
            result.Add(buffer.Slice(position, end).ToString());
            position += end + 1;
        }
        return result.ToImmutable();
    }

    private static partial class Native
    {
        internal sealed class VolumeSearchHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public VolumeSearchHandle() : base(ownsHandle: true) { }
            protected override bool ReleaseHandle() => FindVolumeClose(handle);
        }

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern VolumeSearchHandle FindFirstVolumeW(StringBuilder name, uint length);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FindNextVolumeW(VolumeSearchHandle search, StringBuilder name, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FindVolumeClose(IntPtr search);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumePathNamesForVolumeNameW(string volumeName,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2, SizeParamIndex = 2)] char[] paths,
            uint length, out uint required);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumeInformationW(string root, StringBuilder label, uint labelLength,
            out uint serial, out uint maximumComponentLength, out uint flags, IntPtr fileSystemName, uint fileSystemNameLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetThreadErrorMode(uint mode, out uint previousMode);
        [DllImport("kernel32.dll")]
        internal static extern uint GetThreadErrorMode();
    }
}
