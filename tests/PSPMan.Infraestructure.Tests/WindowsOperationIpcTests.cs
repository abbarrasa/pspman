using System.IO.Pipes;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsOperationIpcTests
{
    private static readonly Guid RequestId = Guid.Parse("10000000-0000-0000-0000-000000000032");
    private static readonly WindowsOperationRequest Request = new(1, RequestId,
        WindowsElevatedOperation.RemoveDevice, @"USBSTOR\DISK&VEN_TEST\SERIAL", new(7, 1));
    private static readonly WindowsOperationProcessIdentity Ui = new(10, 100, @"C:\PSPMan\PSPMan.exe", "S-1-5-21-100", 1, false);
    private static readonly WindowsOperationProcessIdentity Host = Ui with { ProcessId = 20, CreationTime = 200, Elevated = true };

    [Fact]
    public async Task InitiatorAuthenticatesBeforeSendingAndClosesTheSingleUseChannel()
    {
        using var local = new Process(Ui);
        using var expected = new Process(Host);
        var pipe = new Pipe(Host, WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Request.Operation, true)));
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, isInitiator: true);
        var reply = await ipc.ExchangeAsync(Request, expected, TimeSpan.FromSeconds(1));
        Assert.True(reply.Completed);
        Assert.Equal(Request, WindowsDeviceAuthorization.DecodeRequest(pipe.Data.Written.ToArray()));
        Assert.True(pipe.Disposed);
        Assert.True(pipe.OpenedPeer!.Disposed);
        Assert.False(expected.Disposed); // Caller retains the pinned launch handle.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => ipc.ExchangeAsync(Request, expected, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("reused-pid")]
    [InlineData("user")]
    [InlineData("session")]
    [InlineData("image")]
    [InlineData("not-elevated")]
    [InlineData("exited")]
    [InlineData("expected-exited")]
    [InlineData("generic-dotnet")]
    public async Task InitiatorRejectsUntrustedPeersBeforeSendingAnyRequest(string scenario)
    {
        var peer = scenario switch
        {
            "pid" => Host with { ProcessId = 21 },
            "reused-pid" => Host with { CreationTime = 201 },
            "user" => Host with { UserSid = "S-1-5-21-999" },
            "session" => Host with { SessionId = 2 },
            "image" => Host with { ImagePath = @"C:\Other\PSPMan.exe" },
            "not-elevated" => Host with { Elevated = false },
            "generic-dotnet" => Host with { ImagePath = @"C:\dotnet\dotnet.exe" },
            _ => Host
        };
        using var local = new Process(Ui);
        using var expected = new Process(scenario == "generic-dotnet" ? peer : Host) { IsAlive = scenario != "expected-exited" };
        var pipe = new Pipe(peer, []) { PeerAlive = scenario != "exited" };
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ipc.ExchangeAsync(Request, expected, TimeSpan.FromSeconds(1)));
        Assert.Empty(pipe.Data.Written);
        Assert.True(pipe.Disposed);
        Assert.True(pipe.OpenedPeer!.Disposed);
    }

    [Fact]
    public async Task ElevatedHostAuthenticatesInitiatorBeforeReadingAndReturnsOneCorrelatedReply()
    {
        using var local = new Process(Host);
        using var expected = new Process(Ui);
        var pipe = new Pipe(Ui, WindowsDeviceAuthorization.EncodeRequest(Request));
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, false);
        var calls = 0;
        await ipc.RespondAsync(expected, (request, _) =>
        {
            Assert.Equal(Request, request);
            calls++;
            return Task.FromResult(new WindowsOperationReply(1, request.RequestId, request.Operation, true));
        }, TimeSpan.FromSeconds(1));
        Assert.Equal(1, calls);
        Assert.True(WindowsDeviceAuthorization.DecodeReply(pipe.Data.Written.ToArray(), Request).Completed);
        Assert.True(pipe.Disposed);
        Assert.True(pipe.OpenedPeer!.Disposed);
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("reused-pid")]
    [InlineData("image")]
    [InlineData("session")]
    [InlineData("elevated-initiator")]
    [InlineData("local-not-elevated")]
    [InlineData("local-exited")]
    public async Task HostRejectsIncorrectInitiatingProcessWithoutReadingOrResponding(string scenario)
    {
        var peer = scenario switch
        {
            "pid" => Ui with { ProcessId = 11 },
            "reused-pid" => Ui with { CreationTime = 101 },
            "image" => Ui with { ImagePath = @"C:\Other\PSPMan.exe" },
            "session" => Ui with { SessionId = 2 },
            "elevated-initiator" => Ui with { Elevated = true },
            _ => Ui
        };
        using var local = new Process(scenario == "local-not-elevated" ? Ui : Host) { IsAlive = scenario != "local-exited" };
        using var expected = new Process(Ui);
        var pipe = new Pipe(peer, WindowsDeviceAuthorization.EncodeRequest(Request));
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ipc.RespondAsync(expected,
            (_, _) => throw new Xunit.Sdk.XunitException("Untrusted request reached responder"), TimeSpan.FromSeconds(1)));
        Assert.Equal(0, pipe.Data.ReadCount);
        Assert.Empty(pipe.Data.Written);
        Assert.True(pipe.Disposed);
    }

    [Theory]
    [InlineData("request-id")]
    [InlineData("oversize")]
    [InlineData("truncated-prefix")]
    [InlineData("truncated-body")]
    [InlineData("zero-length")]
    public async Task BadFramesNeverReachTheHostResponder(string scenario)
    {
        var payload = WindowsDeviceAuthorization.EncodeRequest(Request);
        payload = scenario switch
        {
            "request-id" => WindowsDeviceAuthorization.EncodeRequest(Request with { RequestId = Guid.NewGuid() }),
            "oversize" => new byte[] { 255, 255, 255, 255 },
            "truncated-prefix" => payload[..3],
            "truncated-body" => payload[..^1],
            _ => new byte[4]
        };
        using var local = new Process(Host);
        using var expected = new Process(Ui);
        var pipe = new Pipe(Ui, payload);
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, false);
        var error = await Record.ExceptionAsync(() => ipc.RespondAsync(expected,
            (_, _) => throw new Xunit.Sdk.XunitException("Malformed request reached responder"), TimeSpan.FromSeconds(1)));
        if (scenario.StartsWith("truncated", StringComparison.Ordinal)) Assert.IsType<EndOfStreamException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.True(pipe.Disposed);
        Assert.Empty(pipe.Data.Written);
        if (scenario == "oversize") Assert.Equal(4, pipe.Data.ReadCount);
    }

    [Theory]
    [InlineData("connect-timeout")]
    [InlineData("read-timeout")]
    [InlineData("cancel")]
    [InlineData("native-query-error")]
    [InlineData("write-error")]
    public async Task AllInterruptedPathsReleasePipeAndPeerHandles(string scenario)
    {
        using var local = new Process(Ui);
        using var expected = new Process(Host);
        var pipe = new Pipe(Host, []) { HangConnect = scenario == "connect-timeout", FailPeer = scenario == "native-query-error" };
        pipe.Data.HangRead = scenario == "read-timeout";
        pipe.Data.FailWrite = scenario == "write-error";
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, true);
        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancel") cancellation.Cancel();
        var error = await Record.ExceptionAsync(() => ipc.ExchangeAsync(Request, expected,
            TimeSpan.FromMilliseconds(40), cancellation.Token));
        if (scenario is "native-query-error" or "write-error") Assert.IsType<IOException>(error);
        else Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.True(pipe.Disposed);
        if (pipe.OpenedPeer is not null) Assert.True(pipe.OpenedPeer.Disposed);
    }

    [Fact]
    public void NamesAndSecurityPolicyAreLocalRestrictedAndUnpredictable()
    {
        var names = Enumerable.Range(0, 100).Select(_ => WindowsOperationIpc.NewPipeName()).ToArray();
        Assert.Equal(100, names.Distinct().Count());
        Assert.All(names, name => Assert.Matches("^PSPMan-operation-[A-F0-9]{64}$", name));
        var policy = WindowsOperationNativePipe.SecurityDescriptor("S-1-5-5-1-2");
        Assert.Contains("D:P", policy);
        Assert.Contains("(D;;GA;;;NU)", policy);
        Assert.Contains("(A;;GA;;;S-1-5-5-1-2)", policy);
        Assert.Contains("(A;;0x0012019b;;;BA)", policy);
        Assert.DoesNotContain(";;;WD)", policy);
        Assert.Throws<ArgumentException>(() => WindowsOperationNativePipe.SecurityDescriptor("S-1-5-5-1-2)(A;;GA;;;WD)"));
        Assert.Throws<ArgumentException>(() => WindowsOperationIpc.ValidatePipeName(@"\\server\pipe\anything"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("operation")]
    [InlineData("truncated")]
    public async Task InitiatorRejectsUncorrelatedOrTruncatedReplies(string scenario)
    {
        var reply = new WindowsOperationReply(1, RequestId, Request.Operation, true);
        if (scenario == "id") reply = reply with { RequestId = Guid.NewGuid() };
        if (scenario == "operation") reply = reply with { Operation = WindowsElevatedOperation.EjectOpticalMedia };
        var frame = WindowsDeviceAuthorization.EncodeReply(reply);
        if (scenario == "truncated") frame = frame[..^1];
        using var local = new Process(Ui);
        using var expected = new Process(Host);
        var pipe = new Pipe(Host, frame);
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, true);
        var error = await Record.ExceptionAsync(() => ipc.ExchangeAsync(Request, expected, TimeSpan.FromSeconds(1)));
        if (scenario == "truncated") Assert.IsType<EndOfStreamException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.True(pipe.Disposed);
        Assert.True(pipe.OpenedPeer!.Disposed);
    }

    [Theory]
    [InlineData("exit-during-read")]
    [InlineData("responder-fails")]
    [InlineData("responder-wrong-id")]
    [InlineData("responder-timeout")]
    public async Task HostClosesOnLateIdentityChangesAndResponderFailures(string scenario)
    {
        using var local = new Process(Host);
        using var expected = new Process(Ui);
        var pipe = new Pipe(Ui, WindowsDeviceAuthorization.EncodeRequest(Request));
        if (scenario == "exit-during-read") pipe.Data.AfterRead = () => expected.IsAlive = false;
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, false);
        var calls = 0;
        var error = await Record.ExceptionAsync(() => ipc.RespondAsync(expected, async (request, token) =>
        {
            calls++;
            if (scenario == "responder-fails") throw new IOException("Responder failed");
            if (scenario == "responder-timeout") await Task.Delay(Timeout.Infinite, token);
            return new(1, Guid.NewGuid(), request.Operation, true);
        }, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(scenario == "exit-during-read" ? 0 : 1, calls);
        switch (scenario)
        {
            case "exit-during-read": Assert.IsType<UnauthorizedAccessException>(error); break;
            case "responder-fails": Assert.IsType<IOException>(error); break;
            case "responder-wrong-id": Assert.IsType<InvalidDataException>(error); break;
            default: Assert.IsAssignableFrom<OperationCanceledException>(error); break;
        }
        Assert.Empty(pipe.Data.Written);
        Assert.True(pipe.Disposed);
        Assert.True(pipe.OpenedPeer!.Disposed);
    }

    [Fact]
    public async Task GenericDotnetProcessCannotAuthenticateEvenWhenBothImagePathsMatch()
    {
        using var local = new Process(Ui with { ImagePath = @"C:\dotnet\dotnet.exe" });
        using var expected = new Process(Host with { ImagePath = local.Identity.ImagePath });
        var pipe = new Pipe(expected.Identity, []);
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ipc.ExchangeAsync(Request, expected, TimeSpan.FromSeconds(1)));
        Assert.Empty(pipe.Data.Written);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121000)]
    public async Task UnboundedTimeoutsAreRejectedAndCloseTheEndpoint(int milliseconds)
    {
        using var local = new Process(Ui);
        using var expected = new Process(Host);
        var pipe = new Pipe(Host, []);
        using var ipc = new WindowsOperationIpc(pipe, local, RequestId, true);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ipc.ExchangeAsync(Request, expected, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.True(pipe.Disposed);
        Assert.Null(pipe.OpenedPeer);
    }

    [Fact]
    public async Task PreparationCreatesOneRandomEndpointAndOwnsOnlyTheInitiatingLease()
    {
        var initiator = new Process(Ui);
        var pipe = new Pipe(Host, WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Request.Operation, true)));
        string? created = null;
        using var attempt = WindowsOperationIpcAttempt.Create(Request, () => initiator, name => { created = name; return pipe; });
        Assert.Equal(attempt.PipeName, created);
        WindowsOperationIpc.ValidatePipeName(attempt.PipeName);
        Assert.Equal(Ui, attempt.Initiator);
        using var host = new Process(Host);
        Assert.True((await attempt.ExchangeAsync(host, TimeSpan.FromSeconds(1))).Completed);
        Assert.True(initiator.Disposed);
        Assert.True(pipe.Disposed);
        Assert.False(host.Disposed);
    }

    [Fact]
    public void PreparationFailureReleasesThePinnedInitiatorAndInvalidRequestsCreateNoResources()
    {
        var initiator = new Process(Ui);
        Assert.Throws<IOException>(() => WindowsOperationIpcAttempt.Create(Request, () => initiator, _ => throw new IOException("Pipe unavailable")));
        Assert.True(initiator.Disposed);
        Assert.Throws<InvalidDataException>(() => WindowsOperationIpcAttempt.Create(Request with { Version = 99 },
            () => throw new Xunit.Sdk.XunitException("Invalid request captured process"), _ => throw new NotSupportedException()));
    }

    [Fact]
    public void NonWindowsCannotOpenWindowsNativePipes()
    {
        if (OperatingSystem.IsWindows()) return; // Real Windows integration is a separately recorded T36 check.
        Assert.Throws<PlatformNotSupportedException>(() => WindowsOperationNativePipe.CreateServer(WindowsOperationIpc.NewPipeName()));
        Assert.Throws<PlatformNotSupportedException>(() => WindowsOperationNativePipe.CreateClient(WindowsOperationIpc.NewPipeName()));
        Assert.Throws<PlatformNotSupportedException>(() => WindowsDeviceAuthorization.PrepareOperationIpc(Request));
    }

    [Fact]
    public async Task RealLocalByteStreamsExchangeOneFrameInEachDirectionWithSimulatedProcessEvidence()
    {
        // This exercises real stream framing, NOT Windows native authentication or elevation.
        // Unix implements named pipes using length-limited socket paths. This is a framing fixture,
        // not the Windows-only endpoint factory, so use a shorter random test name.
        var name = "pspman-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var ui = new Process(Ui);
        using var host = new Process(Host);
        using var initiatingChannel = new WindowsOperationIpc(new StreamPipe(server, Host, server.WaitForConnectionAsync), ui, RequestId, true);
        using var respondingChannel = new WindowsOperationIpc(new StreamPipe(client, Ui, client.ConnectAsync), host, RequestId, false);
        var hostTask = respondingChannel.RespondAsync(ui,
            (request, _) => Task.FromResult(new WindowsOperationReply(1, request.RequestId, request.Operation, true)), TimeSpan.FromSeconds(5));
        var reply = await initiatingChannel.ExchangeAsync(Request, host, TimeSpan.FromSeconds(5));
        await hostTask;
        Assert.True(reply.Completed);
        Assert.False(server.IsConnected);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void ElevatedPreparationIsRejectedBeforeCreatingAnEndpoint()
    {
        var own = new Process(Host);
        Assert.Throws<UnauthorizedAccessException>(() => WindowsOperationIpcAttempt.Create(Request, () => own,
            _ => throw new Xunit.Sdk.XunitException("An elevated UI created a server")));
        Assert.True(own.Disposed);
    }

    private sealed class Process(WindowsOperationProcessIdentity identity) : IWindowsOperationProcess
    {
        public WindowsOperationProcessIdentity Identity => identity;
        public bool IsAlive { get; set; } = true;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Pipe(WindowsOperationProcessIdentity peer, byte[] payload) : IWindowsOperationPipe
    {
        public TestStream Data { get; } = new(payload);
        public Stream Stream => Data;
        public bool Disposed { get; private set; }
        public bool PeerAlive { get; init; } = true;
        public bool HangConnect { get; init; }
        public bool FailPeer { get; init; }
        public Process? OpenedPeer { get; private set; }
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HangConnect) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public IWindowsOperationProcess OpenPeer() => FailPeer ? throw new IOException("Native query failed")
            : OpenedPeer = new(peer) { IsAlive = PeerAlive };
        public void Dispose() { Disposed = true; Data.Dispose(); }
    }

    private sealed class StreamPipe(PipeStream stream, WindowsOperationProcessIdentity peer,
        Func<CancellationToken, Task> connect) : IWindowsOperationPipe
    {
        public Stream Stream => stream;
        public Task ConnectAsync(CancellationToken cancellationToken) => connect(cancellationToken);
        public IWindowsOperationProcess OpenPeer() => new Process(peer);
        public void Dispose() => stream.Dispose();
    }

    private sealed class TestStream(byte[] payload) : Stream
    {
        public List<byte> Written { get; } = [];
        public int ReadCount { get; private set; }
        public bool HangRead { get; set; }
        public bool FailWrite { get; set; }
        public Action? AfterRead { get; set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (HangRead) await Task.Delay(Timeout.Infinite, cancellationToken);
            var count = Math.Min(Math.Min(buffer.Length, 3), payload.Length - ReadCount); // Exercise partial pipe reads.
            payload.AsMemory(ReadCount, count).CopyTo(buffer);
            ReadCount += count;
            AfterRead?.Invoke();
            return count;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (FailWrite) throw new IOException("Disconnected");
            Written.AddRange(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
