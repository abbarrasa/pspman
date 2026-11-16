using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace PSPMan.Infraestructure.Tests;

// Explicitly test-only: no device access, UI, production runtime resolver or copy rules.
internal sealed class NativeArchiveProbe : IDisposable
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PSPMan.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
    internal static string BridgePath => Environment.GetEnvironmentVariable("PSPMAN_TEST_NATIVE_BRIDGE")
        ?? Path.Combine(Root, "native", "PSPMan.ArchiveBridge", "out", OperatingSystem.IsWindows() ? "pspman_archive_bridge.dll" : "libpspman_archive_bridge.so");
    internal static string EnginePath => Environment.GetEnvironmentVariable("PSPMAN_TEST_NATIVE_ENGINE")
        ?? Path.Combine(Path.GetDirectoryName(BridgePath)!, OperatingSystem.IsWindows() ? "7z.dll" : "7z.so");
    internal static bool Available => File.Exists(BridgePath) && File.Exists(EnginePath);
    private static readonly Lazy<Api> Native = new(() => new Api(BridgePath));
    private readonly string name;
    private readonly string directory;
    private readonly Dictionary<ulong, Stream> inputs = [];
    private readonly Dictionary<uint, MemoryStream> outputs = [];
    private readonly HashSet<uint> directories = [];
    private readonly Dictionary<string, byte[]> sourceHashes = [];
    private readonly object gate = new();
    private ulong nextHandle;
    private nint session;
    private Callbacks callbacks;
    public string? Password { get; set; }
    public Dictionary<uint, byte[]> Outputs { get; } = [];
    public List<(uint Index, int Result)> Ended { get; } = [];
    public List<string> OpenedParts { get; } = [];
    public List<(ulong Decoded, ulong Written)> Progress { get; } = [];
    public List<uint> Begun { get; } = [];
    public Func<uint, bool>? Skip { get; set; }
    public Action<NativeArchiveProbe>? OnProgress { get; set; }
    public Action<NativeArchiveProbe>? OnRead { get; set; }
    public Action<NativeArchiveProbe>? OnPassword { get; set; }
    public bool FailRead { get; set; }
    public bool FailWrite { get; set; }
    public Exception? CallbackError { get; private set; }
    public ulong Decoded { get; private set; }
    public ulong Written { get; private set; }
    public int OpenInputCount { get { lock (gate) return inputs.Count; } }
    public int ClosedInputs { get; private set; }

    public NativeArchiveProbe(string name, string? password = null, string? directory = null, uint abi = 1)
    {
        // Test-local part discovery, not an implicit alternative source selection.
        this.name = Regex.Replace(name, @"\.z\d+$", ".zip", RegexOptions.IgnoreCase);
        this.name = Regex.Replace(this.name, @"\.part\d+(\.rar)$", ".part01$1", RegexOptions.IgnoreCase);
        this.name = Regex.Replace(this.name, @"\.r\d+$", ".rar", RegexOptions.IgnoreCase);
        this.directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers");
        Password = password;
        callbacks = new Callbacks
        {
            Size = (uint)Marshal.SizeOf<Callbacks>(), Abi = abi,
            Open = OpenInput, Read = Read, Seek = Seek, Close = Close,
            Password = GetPassword, Begin = Begin, Write = Write, End = End, Progress = ReportProgress
        };
        var path = Encoding.UTF8.GetBytes(Path.GetFullPath(EnginePath));
        var result = Native.Value.Create(path, (uint)path.Length, ref callbacks, out session);
        if (result != 0) throw new InvalidOperationException($"Native create failed: {result}.");
    }
    public static uint Abi => Native.Value.Abi();
    public static uint EngineVersion => Native.Value.Version();
    public int Open()
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var format = name.StartsWith("Rar5", StringComparison.Ordinal) ? 0xCCu :
            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 1u : 3u;
        return Native.Value.Open(session, format, bytes, (uint)bytes.Length);
    }
    public (Entry Entry, string Path)[] Metadata()
    {
        var result = Native.Value.Count(session, out var count);
        if (result != 0) throw new InvalidOperationException($"Native count failed: {result}.");
        var entries = new List<(Entry, string)>();
        for (uint i = 0; i < count; i++)
        {
            var bytes = new byte[65536];
            result = Native.Value.Metadata(session, i, out var entry, bytes, (uint)bytes.Length, out var length);
            if (result != 0) throw new InvalidOperationException($"Native metadata failed: {result}.");
            entries.Add((entry, Encoding.UTF8.GetString(bytes, 0, checked((int)length))));
        }
        return [.. entries];
    }
    public int Extract(uint[]? indices = null)
    {
        foreach (var item in Metadata())
            if ((item.Entry.Flags & 1) != 0) directories.Add(item.Entry.Index);
        return Native.Value.Extract(session, indices, indices is null ? uint.MaxValue : (uint)indices.Length);
    }
    public void Cancel() => Native.Value.Cancel(session);
    private int Guard(Func<int> action)
    {
        try { return action(); }
        catch (Exception e) { CallbackError = e; return 2; }
    }
    private int OpenInput(nint context, nint pointer, uint length, out ulong handle)
    {
        ulong opened = 0;
        var result = Guard(() =>
        {
            var part = Marshal.PtrToStringUTF8(pointer, checked((int)length))!;
            // The test provider only accepts leaf names from its fixture directory.
            if (part is "" or "." or ".." || part.Contains('/') || part.Contains('\\') || part.Contains(':') || part.Contains('\0'))
                throw new IOException("Unconfined part request.");
            var path = Path.Combine(directory, part);
            if (!File.Exists(path)) return 6;
            var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            stream.Position = 0;
            lock (gate)
            {
                sourceHashes.TryAdd(path, hash);
                opened = ++nextHandle;
                inputs.Add(opened, stream);
                OpenedParts.Add(part);
            }
            return 0;
        });
        handle = opened;
        return result;
    }
    private int Read(nint context, ulong handle, nint pointer, uint length, out uint read)
    {
        uint actual = 0;
        var result = Guard(() =>
        {
            if (FailRead) throw new IOException("Injected source read failure.");
            OnRead?.Invoke(this);
            var bytes = new byte[Math.Min(length, 65536)];
            lock (gate) actual = (uint)inputs[handle].Read(bytes);
            Marshal.Copy(bytes, 0, pointer, (int)actual);
            return 0;
        });
        read = actual;
        return result;
    }
    private int Seek(nint context, ulong handle, long offset, uint origin, out ulong position)
    {
        ulong actual = 0;
        var result = Guard(() =>
        {
            if (origin > 2) throw new IOException("Invalid seek origin.");
            lock (gate) actual = checked((ulong)inputs[handle].Seek(offset, (SeekOrigin)origin));
            return 0;
        });
        position = actual;
        return result;
    }
    private void Close(nint context, ulong handle)
    {
        Guard(() =>
        {
            lock (gate)
            {
                if (!inputs.Remove(handle, out var stream)) throw new IOException("Duplicate close.");
                stream.Dispose(); ClosedInputs++;
            }
            return 0;
        });
    }
    private int GetPassword(nint context, nint pointer, uint capacity, out uint length)
    {
        uint actual = 0;
        var result = Guard(() =>
        {
            OnPassword?.Invoke(this);
            if (Password is null) return 10;
            var bytes = Encoding.UTF8.GetBytes(Password);
            if (bytes.Length > capacity) return 2;
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            actual = (uint)bytes.Length;
            Array.Clear(bytes);
            return 0;
        });
        length = actual;
        return result;
    }
    private int Begin(nint context, uint index) => Guard(() =>
    {
        Begun.Add(index);
        if (Skip?.Invoke(index) == true) return 11;
        if (!directories.Contains(index)) outputs.Add(index, new MemoryStream());
        return 0;
    });
    private int Write(nint context, uint index, nint pointer, uint length, out uint written)
    {
        uint actual = 0;
        var result = Guard(() =>
        {
            // The probe buffers tiny known fixtures only, never proprietary game files.
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            if (FailWrite)
            {
                outputs[index].Write(bytes.AsSpan(0, bytes.Length / 2));
                actual = (uint)bytes.Length / 2;
                throw new IOException("Injected destination failure after partial write.");
            }
            outputs[index].Write(bytes);
            actual = length;
            return 0;
        });
        written = actual;
        return result;
    }
    private int End(nint context, uint index, int result) => Guard(() =>
    {
        Ended.Add((index, result));
        if (outputs.Remove(index, out var stream))
        {
            Outputs[index] = stream.ToArray(); stream.Dispose();
        }
        return 0;
    });
    private int ReportProgress(nint context, ulong decoded, ulong written) => Guard(() =>
    {
        lock (gate)
        {
            Decoded = decoded; Written = written;
            Progress.Add((decoded, written));
            OnProgress?.Invoke(this);
        }
        return 0;
    });
    public void Dispose()
    {
        if (session == 0) return;
        Native.Value.Destroy(session);
        session = 0;
        GC.KeepAlive(callbacks);
        if (inputs.Count != 0 || outputs.Count != 0) throw new InvalidOperationException("Native call leaked owned streams.");
        foreach (var (path, expected) in sourceHashes)
            if (!expected.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))
                throw new InvalidOperationException("Native operation modified a source fixture.");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Entry { public uint Index, Flags; public ulong Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public uint Size, Abi;
        public nint Context;
        public OpenCallback Open;
        public ReadCallback Read;
        public SeekCallback Seek;
        public CloseCallback Close;
        public PasswordCallback Password;
        public BeginCallback Begin;
        public WriteCallback Write;
        public EndCallback End;
        public ProgressCallback Progress;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OpenCallback(nint c, nint name, uint length, out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadCallback(nint c, ulong handle, nint data, uint length, out uint read);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SeekCallback(nint c, ulong handle, long offset, uint origin, out ulong position);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseCallback(nint c, ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PasswordCallback(nint c, nint data, uint capacity, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BeginCallback(nint c, uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WriteCallback(nint c, uint index, nint data, uint length, out uint written);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EndCallback(nint c, uint index, int result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProgressCallback(nint c, ulong decoded, ulong written);
    private sealed class Api
    {
        private readonly nint library;
        public readonly VersionCall Abi, Version;
        public readonly CreateCall Create;
        public readonly OpenCall Open;
        public readonly CountCall Count;
        public readonly MetadataCall Metadata;
        public readonly ExtractCall Extract;
        public readonly SessionCall Cancel, Destroy;
        public Api(string path)
        {
            library = NativeLibrary.Load(Path.GetFullPath(path));
            Abi = Get<VersionCall>("psp_abi"); Version = Get<VersionCall>("psp_engine_version");
            Create = Get<CreateCall>("psp_create"); Open = Get<OpenCall>("psp_open");
            Count = Get<CountCall>("psp_count"); Metadata = Get<MetadataCall>("psp_metadata");
            Extract = Get<ExtractCall>("psp_extract"); Cancel = Get<SessionCall>("psp_cancel"); Destroy = Get<SessionCall>("psp_destroy");
        }
        private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VersionCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateCall(byte[] path, uint length, ref Callbacks callbacks, out nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OpenCall(nint session, uint format, byte[] name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountCall(nint session, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MetadataCall(nint session, uint index, out Entry entry, byte[] path, uint capacity, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ExtractCall(nint session, uint[]? indices, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SessionCall(nint session);
}

public sealed class NativeBridgeTheoryAttribute : TheoryAttribute
{
    public NativeBridgeTheoryAttribute()
    {
        if (!NativeArchiveProbe.Available && Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_NATIVE_TESTS") != "1")
            Skip = "T01 native bridge/runtime not built; mandatory native gate remains pending. See native bridge README.";
    }
}
public sealed class NativeBridgeFactAttribute : FactAttribute
{
    public NativeBridgeFactAttribute()
    {
        if (!NativeArchiveProbe.Available && Environment.GetEnvironmentVariable("PSPMAN_REQUIRE_NATIVE_TESTS") != "1")
            Skip = "T01 native bridge/runtime not built; mandatory native gate remains pending. See native bridge README.";
    }
}
