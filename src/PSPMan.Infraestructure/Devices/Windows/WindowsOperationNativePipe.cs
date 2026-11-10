using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Local, first-instance-only Windows pipe with an explicit protected DACL and no impersonation.</summary>
internal sealed class WindowsOperationNativePipe : IWindowsOperationPipe
{
    private readonly string name;
    private readonly bool server;
    private readonly object gate = new();
    private PipeStream? stream;
    private bool disposed;
    public Stream Stream => stream ?? throw new InvalidOperationException("Operation pipe is not connected.");

    private WindowsOperationNativePipe(string name, bool server, PipeStream? stream = null)
    {
        this.name = name;
        this.server = server;
        this.stream = stream;
    }

    internal static string SecurityDescriptor(string logonSid)
    {
        var parts = logonSid?.Split('-');
        if (parts is not { Length: 6 } || parts[0] != "S" || parts[1] != "1" || parts[2] != "5" || parts[3] != "5"
            || parts.Skip(4).Any(part => part.Length == 0 || part.Any(character => !char.IsAsciiDigit(character)) || !uint.TryParse(part, out _)))
            throw new ArgumentException("A native logon SID is required.", nameof(logonSid));
        // Administrative clients need read/write, NOT FILE_CREATE_PIPE_INSTANCE (0x4). The logon
        // owner creates the server; native peer validation rejects every process except the pinned one.
        return $"D:P(D;;GA;;;NU)(A;;GA;;;{logonSid})(A;;0x0012019b;;;BA)";
    }

    internal static WindowsOperationNativePipe CreateServer(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows operation pipes require Windows.");
        WindowsOperationIpc.ValidatePipeName(name);
        using var own = WindowsOperationNativeProcess.Capture((uint)Environment.ProcessId);
        if (own.Identity.Elevated) throw new UnauthorizedAccessException("The initiating process must remain unelevated.");
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(SecurityDescriptor(own.LogonSid), 1, out var descriptor, out _)) throw Error();
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            // DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE | FILE_FLAG_OVERLAPPED; byte mode, reject remote clients.
            var handle = CreateNamedPipeW(@"\\.\pipe\" + name, 0x40080003, 0x8, 1,
                WindowsDeviceAuthorization.MaxPayloadBytes + 4, WindowsDeviceAuthorization.MaxPayloadBytes + 4, 0, ref attributes);
            if (handle.IsInvalid) { var error = Error(); handle.Dispose(); throw error; }
            try { return new(name, true, new NamedPipeServerStream(PipeDirection.InOut, true, false, handle)); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }

    internal static WindowsOperationNativePipe CreateClient(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows operation pipes require Windows.");
        WindowsOperationIpc.ValidatePipeName(name);
        return new(name, false);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        cancellationToken.ThrowIfCancellationRequested();
        if (server)
        {
            await ((NamedPipeServerStream)Stream).WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate) ObjectDisposedException.ThrowIf(disposed, this);
            // Explicit rights exclude pipe-instance creation. SECURITY_SQOS_PRESENT with ANONYMOUS
            // prevents an untrusted server from impersonating this elevated client, even before authentication.
            var handle = CreateFileW(@"\\.\pipe\" + name, 0x0012019b, 0, IntPtr.Zero, 3, 0x40100000, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                NamedPipeClientStream connected;
                try { connected = new(PipeDirection.InOut, true, true, handle); }
                catch { handle.Dispose(); throw; }
                lock (gate)
                {
                    if (disposed) { connected.Dispose(); throw new ObjectDisposedException(nameof(WindowsOperationNativePipe)); }
                    if (stream is not null) { connected.Dispose(); throw new InvalidOperationException("Pipe is already connected."); }
                    stream = connected;
                }
                return;
            }
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is not (2 or 231)) throw new Win32Exception(error); // Missing server or busy: bounded by shared deadline.
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    public IWindowsOperationProcess OpenPeer()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var handle = ((PipeStream)Stream).SafePipeHandle;
        var id = PeerProcessId(handle, server);
        var peer = WindowsOperationNativeProcess.Capture(id);
        try
        {
            if (PeerProcessId(handle, server) != peer.Identity.ProcessId || !peer.IsAlive)
                throw new UnauthorizedAccessException("Operation peer changed while acquiring its process handle.");
            return peer;
        }
        catch { peer.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static uint PeerProcessId(SafePipeHandle pipe, bool server)
    {
        uint id;
        if (!(server ? GetNamedPipeClientProcessId(pipe, out id) : GetNamedPipeServerProcessId(pipe, out id)) || id == 0) throw Error();
        return id;
    }

    public void Dispose()
    {
        PipeStream? closing;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            closing = stream;
            stream = null;
        }
        closing?.Dispose();
    }

    private static Win32Exception Error() => new(Marshal.GetLastPInvokeError());
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        internal int Inherit; // Always zero; handles must not leak to unrelated child processes.
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string descriptor, uint revision, out IntPtr security, out uint size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances,
        int outSize, int inSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafePipeHandle CreateFileW(string name, uint access, uint share, IntPtr attributes, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
}
