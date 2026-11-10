using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceOperationModeTests
{
    private static readonly Guid RequestId = Guid.Parse("10000000-0000-0000-0000-000000000035");
    private static readonly string PipeName = "PSPMan-operation-" + new string('A', 64);
    private static readonly WindowsOperationProcessIdentity Initiator = new(10, 100, @"C:\PSPMan\PSPMan.exe", "S-1-5-21-100", 1, false);
    private static readonly WindowsOperationProcessIdentity Host = Initiator with { ProcessId = 20, CreationTime = 200, Elevated = true };
    private static string[] Arguments() => ["--device-operation", "--pipe", PipeName, "--request", RequestId.ToString("D"), "--initiator", "10", "--initiator-start", "100"];

    [Fact]
    public void OrdinaryArgumentsDoNotInvokeAnyWindowsBootstrap()
    {
        Assert.Null(WindowsDeviceOperationMode.RunIfRequested([], _ => throw new Xunit.Sdk.XunitException("Ordinary startup entered helper")));
        Assert.Null(WindowsDeviceOperationMode.RunIfRequested(["--some-normal-option"], _ => throw new Xunit.Sdk.XunitException("Ordinary startup entered helper")));
    }

    [Fact]
    public void ValidFixedArgumentsRoundTripAndPreserveOperationExitStatus()
    {
        var parsed = WindowsDeviceOperationMode.Parse(Arguments());
        Assert.Equal(PipeName, parsed.PipeName);
        Assert.Equal(RequestId, parsed.RequestId);
        Assert.Equal(10u, parsed.InitiatorProcessId);
        Assert.Equal(100, parsed.InitiatorCreationTime);
        Assert.Equal(0, WindowsDeviceOperationMode.RunIfRequested(Arguments(), _ => 0));
        Assert.Equal(1, WindowsDeviceOperationMode.RunIfRequested(Arguments(), _ => 1));
    }

    [Theory]
    [InlineData(0, "--Device-Operation")]
    [InlineData(0, "--device-operation=1")]
    [InlineData(0, "/device-operation")]
    [InlineData(1, "--unknown")]
    [InlineData(1, "--request")]
    [InlineData(2, @"\\server\pipe\remote")]
    [InlineData(2, "PSPMan-operation-AAAA")]
    [InlineData(3, "--Request")]
    [InlineData(4, "00000000-0000-0000-0000-000000000000")]
    [InlineData(4, "{10000000-0000-0000-0000-000000000035}")]
    [InlineData(6, "0")]
    [InlineData(6, "-1")]
    [InlineData(6, "+10")]
    [InlineData(6, "010")]
    [InlineData(6, "4294967296")]
    [InlineData(6, " 10")]
    [InlineData(8, "0")]
    [InlineData(8, "-100")]
    [InlineData(8, "1e2")]
    [InlineData(8, "0100")]
    [InlineData(8, "9223372036854775808")]
    public void MalformedFieldsNeverReachNativeExecutionOrFallBackToOrdinaryStartup(int index, string value)
    {
        var args = Arguments();
        args[index] = value;
        Assert.Equal(2, WindowsDeviceOperationMode.RunIfRequested(args, _ => throw new Xunit.Sdk.XunitException("Malformed bootstrap executed")));
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("missing-value")]
    [InlineData("extra")]
    [InlineData("oversized")]
    [InlineData("null")]
    public void MissingDuplicateUnknownAndOversizedMessagesFailClosed(string scenario)
    {
        var args = Arguments();
        args = scenario switch
        {
            "missing-mode" => args[1..],
            "missing-value" => args[..^1],
            "extra" => [.. args, "--force"],
            "oversized" => ["--device-operation", new string('x', 10000)],
            _ => ["--device-operation", null!]
        };
        Assert.Equal(2, WindowsDeviceOperationMode.RunIfRequested(args, _ => throw new Xunit.Sdk.XunitException("Invalid bootstrap executed")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\PSPMan\PSPMan.exe")]
    [InlineData(@"C:\Projects\pspman\src\PSPMan.Desktop\bin\Debug\net10.0\PSPMan.Desktop.exe")]
    public void PublishedAndDevelopmentApphostsUseTheSameBoundedBootstrap(string path)
    {
        var spec = WindowsOperationNativeLauncher.BuildSpec(PipeName, RequestId, Initiator with { ImagePath = path });
        var parsed = WindowsDeviceOperationMode.Parse(spec.Arguments.Split(' '));
        Assert.Equal(WindowsDeviceOperationMode.Parse(Arguments()), parsed);
        Assert.Equal(path, spec.FileName); // Spaces in executable paths never enter the bootstrap argument string.
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("session")]
    [InlineData("image")]
    [InlineData("initiator-elevated")]
    [InlineData("host-not-elevated")]
    [InlineData("initiator-dead")]
    [InlineData("host-dead")]
    [InlineData("generic-dotnet")]
    public async Task BootstrapValidatesNativeProcessEvidenceBeforeInvokingTheHost(string scenario)
    {
        using var local = new Peer(Host);
        using var initiating = new Peer(Initiator);
        initiating.Identity = scenario switch
        {
            "pid" => Initiator with { ProcessId = 99 },
            "start" => Initiator with { CreationTime = 101 },
            "session" => Initiator with { SessionId = 2 },
            "image" => Initiator with { ImagePath = @"C:\Other\PSPMan.exe" },
            "initiator-elevated" => Initiator with { Elevated = true },
            "generic-dotnet" => Initiator with { ImagePath = @"C:\dotnet\dotnet.exe" },
            _ => Initiator
        };
        if (scenario == "generic-dotnet") local.Identity = Host with { ImagePath = initiating.Identity.ImagePath };
        if (scenario == "host-not-elevated") local.Identity = Host with { Elevated = false };
        if (scenario == "initiator-dead") initiating.Alive = false;
        if (scenario == "host-dead") local.Alive = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WindowsDeviceOperationMode.RunBootstrapAsync(
            WindowsDeviceOperationMode.Parse(Arguments()), Host.ProcessId, id => id == Host.ProcessId ? local : initiating,
            (_, _, _) => throw new Xunit.Sdk.XunitException("Untrusted initiating process reached host"), CancellationToken.None));
        Assert.True(local.Disposed);
        Assert.True(initiating.Disposed);
    }

    [Fact]
    public async Task ValidNativeBootstrapPinsTheExpectedInitiatorUntilTheHostReturns()
    {
        var local = new Peer(Host);
        var initiating = new Peer(Initiator);
        var result = await WindowsDeviceOperationMode.RunBootstrapAsync(WindowsDeviceOperationMode.Parse(Arguments()), Host.ProcessId,
            id => id == Host.ProcessId ? local : initiating, (bootstrap, pinned, token) =>
            {
                Assert.Same(initiating, pinned);
                Assert.False(initiating.Disposed);
                Assert.Equal(RequestId, bootstrap.RequestId);
                Assert.True(token.CanBeCanceled);
                return Task.FromResult(0);
            }, CancellationToken.None);
        Assert.Equal(0, result);
        Assert.True(local.Disposed);
        Assert.True(initiating.Disposed);
    }

    [Fact]
    public void HelperErrorsCannotTurnIntoOrdinaryStartupOrExposeExceptionText()
    {
        Assert.Equal(2, WindowsDeviceOperationMode.RunIfRequested(Arguments(), _ => throw new IOException("untrusted payload")));
        if (!OperatingSystem.IsWindows()) Assert.Equal(2, WindowsDeviceOperationMode.RunIfRequested(Arguments()));
    }

    private sealed class Peer(WindowsOperationProcessIdentity identity) : IWindowsOperationProcess
    {
        public WindowsOperationProcessIdentity Identity { get; set; } = identity;
        public bool Alive { get; set; } = true;
        public bool IsAlive => Alive && !Disposed;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
