using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

internal enum WindowsCopyOpenMode { Metadata, Directory, Read, Write, CreateFile, CreateDirectory }

/// <summary>One-component directory-relative opens; never passes an archive path to Win32 name parsing.</summary>
internal static class WindowsCopyNative
{
    internal const int NameMissing = unchecked((int)0xc0000034), PathMissing = unchecked((int)0xc000003a),
        Collision = unchecked((int)0xc0000035);
    private const uint ReadAttributes = 0x80, Synchronize = 0x100000;
    internal readonly record struct OpenParameters(uint Access, uint Share, uint Disposition, uint Options);
    internal static OpenParameters Parameters(WindowsCopyOpenMode mode) => new(
        Synchronize | ReadAttributes | (mode switch
        { WindowsCopyOpenMode.Read => 1u, WindowsCopyOpenMode.Write or WindowsCopyOpenMode.CreateFile => 0x40000001u,
            WindowsCopyOpenMode.Directory or WindowsCopyOpenMode.CreateDirectory => 0x20u, _ => 0u }),
        mode is WindowsCopyOpenMode.Metadata or WindowsCopyOpenMode.Read ? 7u : 1u,
        mode is WindowsCopyOpenMode.CreateFile or WindowsCopyOpenMode.CreateDirectory ? 2u : 1u, // FILE_CREATE / FILE_OPEN only
        0x200020u | (mode switch // FILE_OPEN_REPARSE_POINT | FILE_SYNCHRONOUS_IO_NONALERT
        { WindowsCopyOpenMode.Directory or WindowsCopyOpenMode.CreateDirectory => 1u,
            WindowsCopyOpenMode.Read or WindowsCopyOpenMode.Write or WindowsCopyOpenMode.CreateFile => 0x40u, _ => 0u }));

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ObjectAttributes
    { public uint Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct IoStatusBlock { public IntPtr Status, Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInformation
    {
        public uint Attributes;
        private FileTime Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        internal readonly ulong Created => Creation.Value;
        internal readonly ulong Written => Write.Value;
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct BasicInformation
    { [FieldOffset(24)] public long ChangeTime; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInformation { public ulong Serial, IdLow, IdHigh; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FullSizeInformation
    { public long TotalUnits, CallerAvailableUnits, ActualAvailableUnits; public uint Sectors, Bytes; }

    internal sealed record FileStat(string Volume, string FileId, long Length, string Change, uint Attributes, uint Links)
    {
        internal string Identity => Volume + ":" + FileId;
        internal bool IsDirectory => (Attributes & 0x10) != 0;
        internal bool IsReparse => (Attributes & 0x400) != 0;
    }

    internal static (string MountPath, string VolumePath) ResolveMount(string rootPath)
    {
        var mounted = new StringBuilder(32768);
        if (!GetVolumePath(rootPath.EndsWith('\\') ? rootPath : rootPath + "\\", mounted, (uint)mounted.Capacity))
            throw Win32Error("Resolve selected mount path", Marshal.GetLastPInvokeError());
        var volume = new StringBuilder(64);
        if (!GetVolumeName(mounted.ToString(), volume, (uint)volume.Capacity))
            throw Win32Error("Resolve selected mount volume", Marshal.GetLastPInvokeError());
        return (mounted.ToString(), volume.ToString());
    }
    internal static SafeFileHandle OpenSelectedMount(string mountPath, Guid expected)
    {
        var opened = CreateFile(@"\\?\" + mountPath.TrimEnd('\\'), Synchronize | ReadAttributes, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (opened.IsInvalid)
        { var error = Marshal.GetLastPInvokeError(); opened.Dispose(); throw Win32Error("Pin selected volume mount", error); }
        try
        {
            var buffer = new byte[16 * 1024];
            if (!DeviceIoControl(opened, 0x900a8, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var size, IntPtr.Zero))
                throw Win32Error("Read selected volume mount tag", Marshal.GetLastPInvokeError());
            if (size > buffer.Length || !WindowsCopyPathRules.IsSelectedVolumeMount(buffer.AsSpan(0, checked((int)size)), expected))
                throw new IOException("The observed folder is not a mount point for the selected GUID volume.");
            return opened;
        }
        catch { opened.Dispose(); throw; }
    }
    internal static SafeFileHandle OpenGuidVolumeRoot(string volumePath)
    {
        var opened = CreateFile(volumePath, Synchronize | ReadAttributes | 0x20, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (!opened.IsInvalid) return opened;
        var error = Marshal.GetLastPInvokeError(); opened.Dispose(); throw Win32Error("Pin selected GUID volume root", error);
    }

    internal static SafeFileHandle? OpenLeaf(SafeFileHandle parent, string leaf, WindowsCopyOpenMode mode, out int status)
    {
        if (!WindowsCopyPathRules.IsCompatibleName(leaf) || leaf.Length > 32767)
            throw new GameCopyException(new(GameCopyErrorCode.InvalidName, "The Windows filename is incompatible."));
        var parameters = Parameters(mode);
        var text = Marshal.StringToHGlobalUni(leaf);
        var name = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        bool retained = false;
        try
        {
            var unicode = new UnicodeString { Length = checked((ushort)(leaf.Length * 2)), MaximumLength = checked((ushort)(leaf.Length * 2)), Buffer = text };
            Marshal.StructureToPtr(unicode, name, false);
            parent.DangerousAddRef(ref retained);
            var attributes = new ObjectAttributes { Length = (uint)Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(),
                ObjectName = name, Attributes = WindowsCopyPathRules.ObjectAttributesForCaseFlags(CaseFlags(parent)) };
            status = NtCreateFile(out var result, parameters.Access, ref attributes, out _, IntPtr.Zero,
                0x80, parameters.Share, parameters.Disposition, parameters.Options, IntPtr.Zero, 0);
            if (status >= 0 && status != 0x103 && !result.IsInvalid) { status = 0; return result; }
            result.Dispose();
            if (status >= 0) throw new IOException("The synchronous Windows file open did not complete with a valid handle.");
            return null;
        }
        finally { if (retained) parent.DangerousRelease(); Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(text); }
    }

    private static uint CaseFlags(SafeFileHandle parent)
    {
        if (GetCaseInformation(parent, 23, out var flags, 4)) return flags;
        var error = Marshal.GetLastPInvokeError();
        // Unsupported per-directory case flags use Windows' ordinary insensitive lookup.
        if (error is 1 or 50 or 87) return 0;
        throw Win32Error("Read directory case semantics", error);
    }

    internal static FileStat Describe(SafeFileHandle handle)
    {
        if (GetFileType(handle) != 1) throw new IOException("Copy access requires an ordinary disk file or directory.");
        if (!GetFileInformation(handle, out var info)) throw Win32Error("Read file identity", Marshal.GetLastPInvokeError());
        if ((info.Attributes & 0x400) != 0) return new("reparse", "reparse", 0, "", info.Attributes, info.Links);
        if (!GetBasicInformation(handle, 0, out var basic, 40)) throw Win32Error("Read file change evidence", Marshal.GetLastPInvokeError());
        string fileId;
        ulong serial;
        if (GetIdInformation(handle, 18, out var id, 24))
        { serial = id.Serial; fileId = $"128:{id.IdLow:x16}{id.IdHigh:x16}"; }
        else
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is not (1 or 50 or 87)) throw Win32Error("Read extended file identity", error);
            var fs = new StringBuilder(64);
            if (!GetVolumeInformation(handle, null, 0, out _, out _, out _, fs, (uint)fs.Capacity))
                throw Win32Error("Read filesystem identity capabilities", Marshal.GetLastPInvokeError());
            if (fs.ToString().Equals("ReFS", StringComparison.OrdinalIgnoreCase))
                throw new IOException("ReFS requires a supported 128-bit file identifier.");
            serial = info.VolumeSerial; fileId = $"64:{info.IndexHigh:x8}{info.IndexLow:x8}";
        }
        long length;
        try { length = checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow)); }
        catch (OverflowException cause) { throw new GameCopyException(new(GameCopyErrorCode.SizeOverflow, "File size exceeds the supported byte range."), cause); }
        return new(VolumeGuid(handle) + $":{serial:x16}", fileId, length,
            $"{info.Created:x16}:{info.Written:x16}:{basic.ChangeTime:x16}:{info.Attributes:x8}", info.Attributes, info.Links);
    }

    private static string VolumeGuid(SafeFileHandle handle)
    {
        var path = new StringBuilder(512);
        var length = GetFinalPath(handle, path, (uint)path.Capacity, 1); // VOLUME_NAME_GUID
        if (length >= path.Capacity && length < 32768)
        { path.EnsureCapacity(checked((int)length + 1)); length = GetFinalPath(handle, path, (uint)path.Capacity, 1); }
        if (length == 0) throw Win32Error("Resolve held volume GUID", Marshal.GetLastPInvokeError());
        if (length >= path.Capacity) throw new IOException("The held Windows file name exceeds the supported lookup limit.");
        var value = path.ToString();
        var closing = value.IndexOf('}');
        if (!value.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) || closing < 11
            || !Guid.TryParse(value.AsSpan(11, closing - 11), out var guid))
            throw new IOException("The held file does not resolve to a local GUID volume.");
        return guid.ToString("D");
    }

    internal static long? AvailableBytes(SafeFileHandle root)
    {
        var status = NtQueryVolumeInformationFile(root, out var io, out var size, 32, 7);
        if (status != 0) throw NtError("Read held volume capacity", status);
        if (io.Information.ToInt64() < 32) throw new IOException("Windows returned incomplete held-volume capacity information.");
        return WindowsCopyPathRules.AvailableBytes(size.CallerAvailableUnits, size.Sectors, size.Bytes);
    }
    internal static void Truncate(SafeFileHandle handle)
    {
        long zero = 0;
        if (!SetEndOfFile(handle, 6, ref zero, 8)) throw Win32Error("Truncate confirmed output", Marshal.GetLastPInvokeError());
        if (!SetFilePointer(handle, 0, out _, 0)) throw Win32Error("Position confirmed output", Marshal.GetLastPInvokeError());
    }
    internal static bool IsMissing(int status) => status is NameMissing or PathMissing or unchecked((int)0xc000000f);
    internal static void FlushOutput(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle)) throw Win32Error("Flush owned output allocation", Marshal.GetLastPInvokeError());
    }
    internal static GameCopyErrorCode? ClassifiedError(int status) => status switch
    {
        unchecked((int)0xc0000033) or unchecked((int)0xc000003b) or unchecked((int)0xc0000106) => GameCopyErrorCode.InvalidName,
        unchecked((int)0xc00000ba) or unchecked((int)0xc0000103) => GameCopyErrorCode.FileDirectoryConflict,
        _ => null
    };
    internal static IOException NtError(string operation, int status)
    {
        var cause = new Win32Exception(unchecked((int)RtlNtStatusToDosError(status)));
        return new($"{operation}: {cause.Message} (NTSTATUS 0x{unchecked((uint)status):x8}).", cause);
    }
    private static IOException Win32Error(string operation, int error) => new($"{operation}: {new Win32Exception(error).Message} ({error}).", new Win32Exception(error));

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out SafeFileHandle file, uint access, ref ObjectAttributes attributes, out IoStatusBlock io,
        IntPtr allocationSize, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryVolumeInformationFile(SafeFileHandle handle, out IoStatusBlock io, out FullSizeInformation size, uint length, uint infoClass);
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformation(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetBasicInformation(SafeFileHandle handle, int infoClass, out BasicInformation info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIdInformation(SafeFileHandle handle, int infoClass, out FileIdInformation info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCaseInformation(SafeFileHandle handle, int infoClass, out uint flags, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumeInformation(SafeFileHandle handle, StringBuilder? name, uint nameSize,
        out uint serial, out uint componentLength, out uint flags, StringBuilder fs, uint fsSize);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPath(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "GetFileType", ExactSpelling = true, SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumePath(string path, StringBuilder mount, uint length);
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumeName(string mount, StringBuilder volume, uint length);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputLength, byte[] output,
        uint outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetEndOfFile(SafeFileHandle handle, int infoClass, ref long length, uint size);
    [DllImport("kernel32.dll", EntryPoint = "SetFilePointerEx", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFilePointer(SafeFileHandle handle, long offset, out long position, uint origin);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FlushFileBuffers(SafeFileHandle handle);
}
