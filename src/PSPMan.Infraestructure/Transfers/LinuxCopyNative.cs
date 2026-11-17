using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

/// <summary>Linux fixed-width statx/openat2 ABI; no libc-dependent struct stat layout.</summary>
internal static class LinuxCopyNative
{
    internal const int PathOnly = 0x200000, Directory = 0x10000, NoFollow = 0x20000,
        CloseOnExec = 0x80000, NonBlock = 0x800, ReadWrite = 2, Create = 0x40, Exclusive = 0x80;
    internal const int Missing = 2, Exists = 17, NotDirectory = 20, Loop = 40, CrossDevice = 18;
    private const uint RequiredStats = 0x13c5; // type, nlink, mtime, ctime, inode, size, mount id

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenHow { public ulong Flags, Mode, Resolve; }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct Stat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public long Length;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanos;
        [FieldOffset(112)] public long ModifySeconds;
        [FieldOffset(120)] public uint ModifyNanos;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
        [FieldOffset(144)] public ulong MountId;
        public readonly bool IsDirectory => (Mode & 0xf000) == 0x4000;
        public readonly bool IsRegular => (Mode & 0xf000) == 0x8000;
        public readonly bool IsLink => (Mode & 0xf000) == 0xa000;
        public readonly string Identity => $"{MountId}:{DeviceMajor}:{DeviceMinor}:{Inode}";
        public readonly string Change => $"{ChangeSeconds}:{ChangeNanos}:{ModifySeconds}:{ModifyNanos}";
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct FileSystemStats
    {
        [FieldOffset(8)] public ulong FragmentSize;
        [FieldOffset(32)] public ulong AvailableBlocks;
    }

    internal static void RequirePlatform()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Confined Linux copy access requires Linux x64 or arm64 and openat2/statx support.");
    }

    internal static SafeFileHandle? TryOpen(int parent, string path, int flags, out int error, bool root = false)
    {
        if (path.Contains('\0')) throw new GameCopyException(new(GameCopyErrorCode.InvalidName, "A native path contains a null character."));
        try { _ = new UTF8Encoding(false, true).GetByteCount(path); }
        catch (EncoderFallbackException cause) { throw new GameCopyException(new(GameCopyErrorCode.InvalidName, "A native path contains invalid Unicode."), cause); }
        var how = new OpenHow { Flags = (ulong)(flags | CloseOnExec | NoFollow),
            Mode = (flags & Create) != 0 ? 0x180UL : 0, // 0600, subject to umask
            Resolve = root ? 0x04UL : 0x0dUL }; // no symlinks; beneath + no mount crossings below root
        var result = OpenAt2(437, parent, path, ref how, 24);
        error = result < 0 ? Marshal.GetLastPInvokeError() : 0;
        return result < 0 ? null : new SafeFileHandle((IntPtr)result, ownsHandle: true);
    }

    internal static Stat Describe(SafeFileHandle handle)
    {
        if (Statx(handle, "", 0x1000, RequiredStats, out var stat) != 0) throw Error("statx", Marshal.GetLastPInvokeError());
        if ((stat.Mask & RequiredStats) != RequiredStats)
            throw new IOException("The filesystem cannot supply the identity/change evidence required for safe copying.");
        return stat;
    }

    internal static long? AvailableBytes(SafeFileHandle root)
    {
        if (Fstatvfs(root, out var stat) != 0) throw Error("fstatvfs", Marshal.GetLastPInvokeError());
        if (stat.FragmentSize == 0) return null;
        try { return checked((long)checked(stat.AvailableBlocks * stat.FragmentSize)); }
        catch (OverflowException) { return null; }
    }

    internal static void MakeDirectory(SafeFileHandle parent, string name)
    {
        if (MkdirAt(parent, name, 0x1c0) != 0) // 0700, subject to umask
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is 22 or 36) throw new GameCopyException(new(GameCopyErrorCode.InvalidName,
                "The filesystem rejects the directory name."), Error("mkdirat", error));
            if (error != Exists) throw Error("mkdirat", error);
        }
    }
    internal static void Truncate(SafeFileHandle handle)
    {
        if (Ftruncate(handle, 0) != 0) throw Error("ftruncate", Marshal.GetLastPInvokeError());
    }
    internal static void Synchronize(SafeFileHandle root)
    {
        if (SyncFs(root) != 0) throw Error("syncfs", Marshal.GetLastPInvokeError());
    }
    internal static IOException Error(string operation, int error) =>
        new($"{operation}: {new Win32Exception(error).Message} (errno {error}).", new Win32Exception(error));

    // syscall number 437 and these flags are shared by supported x64/arm64 targets.
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long OpenAt2(long number, int parent,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, nuint size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out Stat stat);
    [DllImport("libc", EntryPoint = "fstatvfs", SetLastError = true)]
    private static extern int Fstatvfs(SafeFileHandle handle, out FileSystemStats stat);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkdirAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);
    [DllImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
    private static extern int Ftruncate(SafeFileHandle handle, long length);
    [DllImport("libc", EntryPoint = "syncfs", SetLastError = true)]
    private static extern int SyncFs(SafeFileHandle handle);
}
