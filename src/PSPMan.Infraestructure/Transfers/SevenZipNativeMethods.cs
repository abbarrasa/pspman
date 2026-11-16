using System.Runtime.InteropServices;
namespace PSPMan.Infraestructure.Transfers;

internal static class SevenZipNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Entry { public uint Index, Flags; public ulong Size; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Callbacks
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int OpenCallback(nint c, nint name, uint length, out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ReadCallback(nint c, ulong handle, nint data, uint length, out uint read);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SeekCallback(nint c, ulong handle, long offset, uint origin, out ulong position);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void CloseCallback(nint c, ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PasswordCallback(nint c, nint data, uint capacity, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BeginCallback(nint c, uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int WriteCallback(nint c, uint index, nint data, uint length, out uint written);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int EndCallback(nint c, uint index, int result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ProgressCallback(nint c, ulong decoded, ulong written);
    internal sealed class Api : IDisposable
    {
        internal readonly nint library;
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
            try
            {
            Abi = Get<VersionCall>("psp_abi"); Version = Get<VersionCall>("psp_engine_version");
            Create = Get<CreateCall>("psp_create"); Open = Get<OpenCall>("psp_open");
            Count = Get<CountCall>("psp_count"); Metadata = Get<MetadataCall>("psp_metadata");
            Extract = Get<ExtractCall>("psp_extract"); Cancel = Get<SessionCall>("psp_cancel"); Destroy = Get<SessionCall>("psp_destroy");
            }
            catch { NativeLibrary.Free(library); throw; }
        }
        public void Dispose() => NativeLibrary.Free(library);
        internal T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint VersionCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateCall(byte[] path, uint length, ref Callbacks callbacks, out nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int OpenCall(nint session, uint format, byte[] name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CountCall(nint session, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int MetadataCall(nint session, uint index, out Entry entry, byte[] path, uint capacity, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ExtractCall(nint session, uint[]? indices, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SessionCall(nint session);
}
