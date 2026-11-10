using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>OS observations, never deserialized from a request. CreationTime is the native FILETIME.</summary>
internal sealed record WindowsOperationProcessIdentity(uint ProcessId, long CreationTime, string ImagePath,
    string UserSid, uint SessionId, bool Elevated);

/// <summary>A caller-owned, pinned process handle; keep it open throughout the IPC exchange.</summary>
internal interface IWindowsOperationProcess : IDisposable
{
    WindowsOperationProcessIdentity Identity { get; }
    bool IsAlive { get; }
}

internal interface IWindowsOperationPipe : IDisposable
{
    Stream Stream { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    IWindowsOperationProcess OpenPeer();
}

/// <summary>
/// One authenticated request/reply. The initiator owns the server, the elevated host is the client.
/// Local and expected peer leases are borrowed, not owned. Expected host evidence must originate from
/// the launch handle; host bootstrap must pin and verify the initiating process before constructing this channel.
/// No operation is selected by this transport; the typed responder is supplied by the future operation host.
/// </summary>
internal sealed class WindowsOperationIpc(IWindowsOperationPipe pipe, IWindowsOperationProcess local,
    Guid requestId, bool isInitiator) : IDisposable
{
    private const string PipePrefix = "PSPMan-operation-";
    private int started;
    private int disposed;

    internal static string NewPipeName() => PipePrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal static void ValidatePipeName(string name)
    {
        if (name is null || name.Length != PipePrefix.Length + 64 || !name.StartsWith(PipePrefix, StringComparison.Ordinal)
            || name.AsSpan(PipePrefix.Length).ContainsAnyExcept("0123456789ABCDEF"))
            throw new ArgumentException("Invalid local operation pipe name.", nameof(name));
    }

    internal async Task<WindowsOperationReply> ExchangeAsync(WindowsOperationRequest request,
        IWindowsOperationProcess expectedHost, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Begin();
        try
        {
            if (!isInitiator) throw new InvalidOperationException("This is not an initiating channel.");
            var frame = WindowsDeviceAuthorization.EncodeRequest(request);
            if (request.RequestId != requestId) throw WindowsOperationProtocol.Invalid();
            using var deadline = Deadline(timeout, cancellationToken);
            using var abort = deadline.Token.Register(Dispose);
            deadline.Token.ThrowIfCancellationRequested();
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            using var peer = pipe.OpenPeer();
            Authenticate(expectedHost, peer);
            deadline.Token.ThrowIfCancellationRequested();
            await pipe.Stream.WriteAsync(frame, deadline.Token).ConfigureAwait(false);
            var response = await ReadFrameAsync(deadline.Token).ConfigureAwait(false);
            return WindowsDeviceAuthorization.DecodeReply(response, request);
        }
        finally { Dispose(); }
    }

    internal async Task RespondAsync(IWindowsOperationProcess expectedInitiator,
        Func<WindowsOperationRequest, CancellationToken, Task<WindowsOperationReply>> respond,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Begin();
        try
        {
            if (isInitiator) throw new InvalidOperationException("This is not an operation-host channel.");
            ArgumentNullException.ThrowIfNull(respond);
            using var deadline = Deadline(timeout, cancellationToken);
            using var abort = deadline.Token.Register(Dispose);
            deadline.Token.ThrowIfCancellationRequested();
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            using var peer = pipe.OpenPeer();
            Authenticate(expectedInitiator, peer); // No payload reads until native peer verification succeeds.
            var request = WindowsDeviceAuthorization.DecodeRequest(await ReadFrameAsync(deadline.Token).ConfigureAwait(false));
            if (request.RequestId != requestId || requestId == Guid.Empty) throw WindowsOperationProtocol.Invalid();
            Authenticate(expectedInitiator, peer); // Initiator may have exited during the read.
            deadline.Token.ThrowIfCancellationRequested();
            var reply = await respond(request, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            var frame = WindowsDeviceAuthorization.EncodeReply(reply);
            WindowsDeviceAuthorization.DecodeReply(frame, request); // Reject responder correlation errors as well.
            await pipe.Stream.WriteAsync(frame, deadline.Token).ConfigureAwait(false);
        }
        finally { Dispose(); }
    }

    private void Authenticate(IWindowsOperationProcess expected, IWindowsOperationProcess peer)
    {
        var own = local.Identity;
        var actual = peer.Identity;
        var anchor = expected.Identity;
        if (!local.IsAlive || !expected.IsAlive || !peer.IsAlive || requestId == Guid.Empty
            || actual != anchor || actual.ProcessId == own.ProcessId || actual.CreationTime <= 0
            || actual.ProcessId == 0 || own.CreationTime <= 0 || string.IsNullOrWhiteSpace(actual.UserSid)
            || actual.SessionId != own.SessionId || actual.Elevated != isInitiator || own.Elevated == isInitiator
            || !string.Equals(actual.ImagePath, own.ImagePath, StringComparison.OrdinalIgnoreCase)
            || !IsDedicatedImage(own.ImagePath))
            throw new UnauthorizedAccessException("The operation pipe peer could not be authenticated.");
    }

    private static bool IsDedicatedImage(string path)
    {
        // An unrestricted dotnet host path does not identify the PSPMan assembly. Development dispatch
        // must provide a separately verified launch strategy in T35; never silently trust dotnet.exe.
        var file = path.Split('\\', '/').LastOrDefault();
        return path.Length is > 3 and <= 32767 && path[1] == ':' && path[2] == '\\'
            && !string.Equals(file, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(file, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await pipe.Stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is 0 or > WindowsDeviceAuthorization.MaxPayloadBytes) throw WindowsOperationProtocol.Invalid();
        var frame = new byte[4 + (int)length];
        header.CopyTo(frame, 0);
        await pipe.Stream.ReadExactlyAsync(frame.AsMemory(4), cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static CancellationTokenSource Deadline(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(timeout), "An operation IPC timeout must be positive and at most two minutes.");
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private void Begin()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Operation channels are single-use.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) pipe.Dispose();
    }
}
