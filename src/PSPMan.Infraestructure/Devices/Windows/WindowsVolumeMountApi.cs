using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Windows drive-letter mounting without changing disk/automount policy or replacing mappings.</summary>
public sealed class WindowsVolumeMountApi : IWindowsVolumeMountApi
{
    internal static bool IsAssignablePartition(ReadOnlySpan<byte> bytes)
    {
        // PARTITION_INFORMATION_EX: union offset 32, GPT attributes offset 64 (Win32 ABI).
        if (bytes.Length < 144 || bytes[29] != 0) return false;
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes) switch
        {
            0 => bytes[34] != 0 && bytes[32] is 0x01 or 0x04 or 0x06 or 0x07 or 0x0b or 0x0c or 0x0e,
            1 => new Guid(bytes.Slice(32, 16)) == new Guid("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7")
                && (BinaryPrimitives.ReadUInt64LittleEndian(bytes[64..]) & 0xC000000000000001UL) == 0,
            // A superfloppy has no partition table; availability was established from its filesystem.
            2 => true,
            _ => false
        };
    }

    public ValueTask<WindowsDriveLetterObservation> GetOccupiedDriveLettersAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        return new(Task.Run(() => ReadOccupiedLetters(cancellationToken), cancellationToken));
    }

    public ValueTask<WindowsDeviceError?> AssignRootAsync(WindowsVolumeMountTarget target, string root,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (root is not { Length: 3 } || root[0] is < 'A' or > 'Z' || root[1] != ':' || root[2] != '\\')
            return ValueTask.FromResult<WindowsDeviceError?>(new("InvalidMountRoot", "SetVolumeMountPoint"));
        return new(Task.Run(async () =>
        {
            // The mutating boundary revalidates too: callers cannot bypass eligibility with arbitrary paths.
            var snapshot = await new WindowsDeviceApi().EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var error = WindowsVolumeMountService.ValidateTarget(snapshot, target, out var volume);
            if (error is not null || volume!.IsMounted) return error;
            using var handle = CreateFileW(target.VolumeGuidPath.TrimEnd('\\'), 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) return LastError("OpenMountVolume");
            var number = new byte[12];
            if (!DeviceIoControl(handle, 0x2D1080, IntPtr.Zero, 0, number, (uint)number.Length, out var returned, IntPtr.Zero))
                return LastError("IOCTL_STORAGE_GET_DEVICE_NUMBER");
            if (returned < number.Length || BinaryPrimitives.ReadUInt32LittleEndian(number) != target.StorageNumber.DeviceType
                || BinaryPrimitives.ReadUInt32LittleEndian(number.AsSpan(4)) != target.StorageNumber.DeviceNumber)
                return new WindowsDeviceError("TargetChanged", "MountVolume");
            if (target.StorageNumber.DeviceType != 2) // FILE_DEVICE_CD_ROM has no disk partition policy.
            {
                var partition = new byte[144];
                if (!DeviceIoControl(handle, 0x70048, IntPtr.Zero, 0, partition, (uint)partition.Length, out returned, IntPtr.Zero))
                    return LastError("IOCTL_DISK_GET_PARTITION_INFO_EX");
                if (returned > partition.Length || !IsAssignablePartition(partition.AsSpan(0, (int)returned)))
                    return new WindowsDeviceError("IneligiblePartition", "MountVolume");
            }
            var letters = ReadOccupiedLetters(cancellationToken);
            if (letters.Error is not null) return letters.Error;
            if ((letters.OccupiedMask & (1u << (root[0] - 'A'))) != 0)
                return new WindowsDeviceError("DriveLetterConflict", "SetVolumeMountPoint", 183);
            cancellationToken.ThrowIfCancellationRequested();
            // Never delete an existing mount point, redefine a DOS device, or retry by overwriting.
            return SetVolumeMountPointW(root, target.VolumeGuidPath) ? null : LastError("SetVolumeMountPoint");
        }, cancellationToken));
    }

    private static WindowsDriveLetterObservation ReadOccupiedLetters(CancellationToken token)
    {
        var mask = GetLogicalDrives();
        if (mask == 0) return new(0, LastError("GetLogicalDrives"));
        var buffer = new char[1024];
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            token.ThrowIfCancellationRequested();
            if ((mask & (1u << (letter - 'A'))) != 0) continue;
            if (QueryDosDeviceW($"{letter}:", buffer, (uint)buffer.Length) != 0)
                mask |= 1u << (letter - 'A');
            else
            {
                var code = Marshal.GetLastPInvokeError();
                if (code == 122) mask |= 1u << (letter - 'A'); // A mapping exists, even if its target list is long.
                else if (code != 2) return new(mask, Error("QueryDosDevice", code));
            }
        }
        return new(mask);
    }

    private static WindowsDeviceError LastError(string operation) => Error(operation, Marshal.GetLastPInvokeError());
    private static WindowsDeviceError Error(string operation, int code) => new(code switch
    {
        5 => "AccessDenied",
        1314 => "PrivilegeNotHeld",
        85 or 183 or 145 => "DriveLetterConflict",
        21 or 1112 => "NoMedia",
        _ => "NativeFailure"
    }, operation, code, new Win32Exception(code).Message);

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows volume mounting requires Windows.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string name, [Out] char[] target, uint length);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVolumeMountPointW(string root, string volumeGuidPath);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
