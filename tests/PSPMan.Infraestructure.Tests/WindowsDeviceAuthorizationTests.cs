using System.ComponentModel;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsDeviceAuthorizationTests
{
    private const string DeviceId = @"USBSTOR\DISK&VEN_TEST\SERIAL";
    private const string VolumeGuid = @"\\?\Volume{00000000-0000-0000-0000-000000000034}\";
    private static readonly Guid RequestId = Guid.Parse("10000000-0000-0000-0000-000000000034");
    private static readonly WindowsDeviceRemovalFailure PrivilegeFailure = new(new("AccessDenied", "NativeOperation", 5));
    private static readonly WindowsOperationProcessIdentity Ui = new(10, 100, @"C:\PSPMan App\PSPMan.exe", "S-1-5-21-100", 1, false);

    [Fact]
    public async Task OrdinarySuccessDoesNotPrepareOrLaunchAndUsesFreshObservedRoots()
    {
        var fixture = new Fixture();
        fixture.SetMounted();
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, null);
        Assert.True(result.Succeeded);
        Assert.False(result.AuthorizationAttempted);
        Assert.Equal(0, fixture.Launcher.Calls);
        Assert.Empty(fixture.Attempts);
        Assert.Equal("Y:\\", Assert.Single(result.Snapshot.Volumes[0].MountPoints));
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("ordinary")]
    [InlineData("configuration-five")]
    public async Task NonPrivilegeFailuresNeverLaunchEvenWhenNumericCodesOverlap(string scenario)
    {
        var fixture = new Fixture();
        var failure = scenario switch
        {
            "busy" => new WindowsDeviceRemovalFailure(new("VolumeBusy", "FSCTL_LOCK_VOLUME", 5)),
            "configuration-five" => new(new("ConfigurationManagerFailure", "CM_Request_Device_Eject", 5)) { ConfigurationStatus = 5 },
            _ => new(new("UnsupportedFileSystem", "MountVolume", 50))
        };
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, failure);
        Assert.False(result.Succeeded);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(0, fixture.Launcher.Calls);
        Assert.Empty(fixture.Attempts);
        Assert.True(fixture.Inventory.Reads > 0);
    }

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume)]
    [InlineData(WindowsElevatedOperation.RemoveDevice)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia)]
    public async Task PrivilegeFailurePreparesIpcBeforeLaunchingAndConfirmsIndependentState(WindowsElevatedOperation operation)
    {
        var fixture = new Fixture(operation);
        fixture.Exchange = () =>
        {
            if (operation == WindowsElevatedOperation.MountVolume) fixture.SetMounted();
            else if (operation == WindowsElevatedOperation.RemoveDevice) fixture.Inventory.Snapshot = new([], [], []);
            else { fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with { Volumes = [] }; fixture.Optical.Present = false; }
            return new(1, RequestId, operation, true);
        };
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.True(result.Succeeded);
        Assert.True(result.AuthorizationAttempted);
        Assert.Equal(1, fixture.Launcher.Calls);
        Assert.True(Assert.Single(fixture.Attempts).Disposed);
        Assert.True(fixture.Launcher.Process.Disposed);
        Assert.True(fixture.Inventory.Reads >= 2);
        Assert.Equal(0, fixture.Optical.Active);
    }

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume)]
    [InlineData(WindowsElevatedOperation.RemoveDevice)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia)]
    public async Task HostSuccessAloneCannotFabricateMountedRemovedOrEmptyState(WindowsElevatedOperation operation)
    {
        var fixture = new Fixture(operation);
        if (operation == WindowsElevatedOperation.EjectOpticalMedia) fixture.Inventory.Snapshot = fixture.Inventory.Snapshot with { Volumes = [] };
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Equal(1, fixture.Launcher.Calls);
        Assert.Equal(0, fixture.Optical.Active);
    }

    [Theory]
    [InlineData(1223, "AuthorizationCancelled")]
    [InlineData(5, "AuthorizationDenied")]
    [InlineData(2, "AuthorizationLaunchFailed")]
    public async Task DenialOrLaunchFailureRefreshesStateSuppressesAutomaticPromptsAndAllowsExplicitRetry(int nativeCode, string code)
    {
        var fixture = new Fixture();
        fixture.Launcher.Error = new Win32Exception(nativeCode);
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.False(result.Succeeded);
        Assert.Equal(code, result.Failure!.Error.Code);
        Assert.Equal(nativeCode, result.Failure.Error.NativeCode);
        Assert.True(Assert.Single(fixture.Attempts).Disposed);
        result = await fixture.Authorization.AuthorizeAsync(fixture.Request with { RequestId = Guid.NewGuid() }, PrivilegeFailure);
        Assert.False(result.Succeeded);
        Assert.Equal(1, fixture.Launcher.Calls);
        Assert.Single(fixture.Attempts);
        await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure, explicitRetry: true);
        Assert.Equal(2, fixture.Launcher.Calls);
        Assert.Equal(2, fixture.Attempts.Count);
    }

    [Theory]
    [InlineData("ipc")]
    [InlineData("peer")]
    [InlineData("correlation")]
    [InlineData("refresh")]
    [InlineData("host-failure")]
    public async Task ErrorsRemainErrorsEvenIfTheDeviceBecomesMountedConcurrently(string scenario)
    {
        var fixture = new Fixture();
        fixture.Exchange = () =>
        {
            fixture.SetMounted();
            if (scenario == "ipc") throw new IOException("Disconnected");
            if (scenario == "peer") throw new UnauthorizedAccessException("Wrong peer");
            if (scenario == "refresh") fixture.Inventory.Fail = true;
            return scenario == "host-failure" ? new(1, RequestId, fixture.Request.Operation, false, PrivilegeFailure)
                : new(1, scenario == "correlation" ? Guid.NewGuid() : RequestId, fixture.Request.Operation, true);
        };
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.True(Assert.Single(fixture.Attempts).Disposed);
        Assert.True(fixture.Launcher.Process.Disposed);
        if (scenario == "refresh") Assert.Empty(result.Snapshot.Volumes);
        else Assert.Equal("Y:\\", Assert.Single(result.Snapshot.Volumes[0].MountPoints));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("launch")]
    [InlineData("exchange")]
    public async Task CancellationRefreshesActualStateAndClosesOwnedResources(string stage)
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        if (stage == "before") cancellation.Cancel();
        if (stage == "launch") fixture.Launcher.OnLaunch = cancellation.Cancel;
        if (stage == "exchange") fixture.Exchange = () => { fixture.SetMounted(); cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure, cancellationToken: cancellation.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("AuthorizationCancelled", result.Failure!.Error.Code);
        Assert.All(fixture.Attempts, attempt => Assert.True(attempt.Disposed));
        Assert.True(fixture.Inventory.Reads > 0);
        if (stage != "before") Assert.True(fixture.Launcher.Process.Disposed);
    }

    [Fact]
    public async Task CancellationDuringFinalRefreshCannotReturnSuccessfulCompletion()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Exchange = () => { fixture.SetMounted(); return new(1, RequestId, fixture.Request.Operation, true); };
        fixture.Inventory.BeforeRead = count => { if (count == 2) cancellation.Cancel(); };
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure, cancellationToken: cancellation.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("AuthorizationCancelled", result.Failure!.Error.Code);
        Assert.Equal("Y:\\", Assert.Single(result.Snapshot.Volumes[0].MountPoints));
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("missing")]
    [InlineData("reformatted")]
    public async Task InvalidTargetsNeverOpenUac(string scenario)
    {
        var fixture = new Fixture();
        var snapshot = fixture.Inventory.Snapshot;
        fixture.Inventory.Snapshot = scenario switch
        {
            "missing" => new([], [], []),
            "internal" => snapshot with { Devices = [snapshot.Devices[0] with { IsRemovable = false, Bus = WindowsStorageBus.Sata }] },
            _ => snapshot with { Volumes = [snapshot.Volumes[0] with { FileSystemSerialNumber = 99 }] }
        };
        Assert.False((await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure)).Succeeded);
        Assert.Equal(0, fixture.Launcher.Calls);
    }

    [Fact]
    public async Task AConfirmedDisconnectCanClearSuppressionButRootChangesCannot()
    {
        var fixture = new Fixture();
        fixture.Launcher.Error = new Win32Exception(1223);
        await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        fixture.SetMounted();
        await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.Equal(1, fixture.Launcher.Calls);
        fixture.Authorization.ForgetDevice(DeviceId);
        await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.Equal(2, fixture.Launcher.Calls);
    }

    [Fact]
    public async Task CommunicationTimeoutIsBoundedEvenWhenTheTransportDoesNotComplete()
    {
        var fixture = new Fixture(timeout: TimeSpan.FromMilliseconds(60));
        fixture.AsyncExchange = _ => new TaskCompletionSource<WindowsOperationReply>().Task;
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.False(result.Succeeded);
        Assert.Equal("AuthorizationTimedOut", result.Failure!.Error.Code);
        Assert.True(Assert.Single(fixture.Attempts).Disposed);
        Assert.True(fixture.Launcher.Process.Disposed);
        Assert.Equal(1, fixture.Launcher.Calls);
        await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.Equal(1, fixture.Launcher.Calls);
    }

    [Fact]
    public async Task ObservationTimeoutCannotReturnStaleUsableRoots()
    {
        var fixture = new Fixture(observationTimeout: TimeSpan.FromMilliseconds(40));
        fixture.SetMounted();
        fixture.Inventory.Hang = true;
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, null);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Snapshot.Volumes);
        Assert.Equal("ObservationUnavailable", Assert.Single(result.Snapshot.Errors).Code);
        Assert.Equal(0, fixture.Launcher.Calls);
    }

    [Fact]
    public async Task ConcurrentAutomaticNotificationsCannotLaunchASecondAttempt()
    {
        var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<WindowsOperationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.AsyncExchange = _ => { started.SetResult(); return reply.Task; };
        var first = fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = fixture.Authorization.AuthorizeAsync(fixture.Request with { RequestId = Guid.NewGuid() }, PrivilegeFailure);
        Assert.Equal(1, fixture.Launcher.Calls);
        reply.SetResult(new(1, RequestId, fixture.Request.Operation, false, PrivilegeFailure));
        await Task.WhenAll(first, second);
        Assert.Equal(1, fixture.Launcher.Calls);
        Assert.Single(fixture.Attempts);
    }

    [Fact]
    public async Task SetupFailureDoesNotLeaveAnAutomaticPromptLoop()
    {
        var fixture = new Fixture();
        fixture.PrepareError = new IOException("Cannot create endpoint");
        var result = await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.False(result.Succeeded);
        Assert.False(result.AuthorizationAttempted);
        await fixture.Authorization.AuthorizeAsync(fixture.Request, PrivilegeFailure);
        Assert.Equal(0, fixture.Launcher.Calls);
        Assert.Equal(1, fixture.Preparations);
    }

    [Fact]
    public void ExecutableAtDriveRootUsesAnAbsoluteWorkingDirectory()
    {
        var spec = WindowsOperationNativeLauncher.BuildSpec(WindowsOperationIpc.NewPipeName(), RequestId, Ui with { ImagePath = @"C:\PSPMan.exe" });
        Assert.Equal(@"C:\", spec.Directory);
    }

    [Fact]
    public void LaunchSpecIsFixedPurposeAndNeverContainsDeviceOrShellArguments()
    {
        var pipe = WindowsOperationIpc.NewPipeName();
        var spec = WindowsOperationNativeLauncher.BuildSpec(pipe, RequestId, Ui);
        Assert.Equal(Ui.ImagePath, spec.FileName);
        Assert.Equal("runas", spec.Verb);
        Assert.Equal(0x540u, spec.Mask);
        Assert.Equal(0, spec.Show);
        Assert.Equal($"--device-operation --pipe {pipe} --request {RequestId:D} --initiator 10 --initiator-start 100", spec.Arguments);
        Assert.DoesNotContain(DeviceId, spec.Arguments);
        Assert.DoesNotContain(VolumeGuid, spec.Arguments);
        Assert.DoesNotContain("cmd", spec.Arguments);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData(@"C:\dotnet\dotnet.exe")]
    [InlineData(@"\\server\share\PSPMan.exe")]
    [InlineData("https://example.com/PSPMan.exe")]
    [InlineData("C:\\PSPMan.exe\" --unsafe")]
    public void UnsupportedOrInjectedExecutableFormsAreRejected(string path) =>
        Assert.Throws<ArgumentException>(() => WindowsOperationNativeLauncher.BuildSpec(WindowsOperationIpc.NewPipeName(), RequestId, Ui with { ImagePath = path }));

    [Fact]
    public async Task CancelledNativeWaitDisposesLateHandlesAndDoesNotOpenParallelPrompts()
    {
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new Peer();
        var first = WindowsOperationLaunchWorker.RunAsync(() =>
        {
            started.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture did not release launch");
            return late;
        }, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var nextCalls = 0;
        var second = WindowsOperationLaunchWorker.RunAsync(() => { Interlocked.Increment(ref nextCalls); return new Peer(); }, CancellationToken.None);
        try
        {
            Assert.Equal(0, Volatile.Read(ref nextCalls));
            Assert.False(second.IsCompleted);
        }
        finally { release.Set(); }
        using var next = await second.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(late.Disposed);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task PreCancelledWorkerNeverInvokesNativeLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WindowsOperationLaunchWorker.RunAsync(
            () => throw new Xunit.Sdk.XunitException("Cancelled launch reached native API"), cancellation.Token));
    }

    private sealed class Fixture
    {
        public Inventory Inventory { get; } = new();
        public Launcher Launcher { get; } = new();
        public Optical Optical { get; } = new();
        public List<Attempt> Attempts { get; } = [];
        public WindowsOperationRequest Request { get; }
        public WindowsDeviceAuthorization Authorization { get; }
        public Func<WindowsOperationReply> Exchange { get; set; }
        public Func<CancellationToken, Task<WindowsOperationReply>>? AsyncExchange { get; set; }
        public Exception? PrepareError { get; set; }
        public int Preparations { get; private set; }
        public Fixture(WindowsElevatedOperation operation = WindowsElevatedOperation.MountVolume, TimeSpan? timeout = null, TimeSpan? observationTimeout = null)
        {
            var number = new WindowsStorageNumber(operation == WindowsElevatedOperation.EjectOpticalMedia ? 2u : 7u, 1);
            Request = new(1, RequestId, operation, DeviceId, number, operation == WindowsElevatedOperation.MountVolume ? VolumeGuid : null,
                operation == WindowsElevatedOperation.MountVolume ? 42u : null);
            Inventory.Snapshot = new([new(DeviceId, null, "device-interface", "Reader", number, WindowsStorageBus.Usb,
                operation == WindowsElevatedOperation.EjectOpticalMedia, true, true)],
                [new("volume-interface", number) { VolumeGuidPath = VolumeGuid, FileSystemSerialNumber = 42, State = WindowsVolumeState.Available }], []);
            Exchange = () => new(1, RequestId, operation, true);
            Launcher.OnLaunch = () => Assert.NotEmpty(Attempts); // Endpoint must exist before launch.
            Authorization = new(Inventory, Launcher, request =>
            {
                Preparations++;
                if (PrepareError is not null) throw PrepareError;
                var attempt = new Attempt(request, token => AsyncExchange?.Invoke(token) ?? Task.FromResult(Exchange()));
                Attempts.Add(attempt);
                return attempt;
            }, Optical, timeout ?? TimeSpan.FromSeconds(2), observationTimeout ?? TimeSpan.FromSeconds(1));
        }
        public void SetMounted() => Inventory.Snapshot = Inventory.Snapshot with
        { Volumes = [Inventory.Snapshot.Volumes[0] with { MountPoints = ["Y:\\"] }] };
    }

    private sealed class Inventory : IWindowsDeviceApi
    {
        public WindowsStorageSnapshot Snapshot { get; set; } = new([], [], []);
        public bool Fail { get; set; }
        public bool Hang { get; set; }
        public Action<int>? BeforeRead { get; set; }
        public int Reads { get; private set; }
        public ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            BeforeRead?.Invoke(Reads);
            cancellationToken.ThrowIfCancellationRequested();
            if (Hang) return new(new TaskCompletionSource<WindowsStorageSnapshot>().Task);
            if (Fail) throw new IOException("Refresh failed");
            return ValueTask.FromResult(Snapshot);
        }
    }
    private sealed class Peer : IWindowsOperationProcess
    {
        public WindowsOperationProcessIdentity Identity => Ui with { ProcessId = 20, CreationTime = 200, Elevated = true };
        public bool IsAlive => !Disposed;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    private sealed class Launcher : IWindowsOperationLauncher
    {
        public int Calls { get; private set; }
        public Peer Process { get; private set; } = new();
        public Exception? Error { get; set; }
        public Action? OnLaunch { get; set; }
        public Task<IWindowsOperationProcess> LaunchAsync(string pipeName, Guid requestId, WindowsOperationProcessIdentity initiator, CancellationToken cancellationToken)
        {
            Calls++;
            OnLaunch?.Invoke();
            if (Error is not null) throw Error;
            Process = new();
            return Task.FromResult<IWindowsOperationProcess>(Process);
        }
    }
    private sealed class Attempt(WindowsOperationRequest request, Func<CancellationToken, Task<WindowsOperationReply>> exchange) : IWindowsOperationAttempt
    {
        public string PipeName { get; } = WindowsOperationIpc.NewPipeName();
        public WindowsOperationProcessIdentity Initiator => Ui;
        public bool Disposed { get; private set; }
        public Task<WindowsOperationReply> ExchangeAsync(IWindowsOperationProcess expectedHost, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Assert.Equal(20u, expectedHost.Identity.ProcessId);
            Assert.True(expectedHost.Identity.Elevated);
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(request.RequestId, RequestId);
            return exchange(cancellationToken);
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class Optical : IWindowsOpticalNativeApi
    {
        public bool Present { get; set; } = true;
        public int Active { get; private set; }
        public IWindowsOpticalHandle? Open(string path, out WindowsDeviceError? error) { error = null; Active++; return new Handle(this); }
        private sealed class Handle(Optical owner) : IWindowsOpticalHandle
        {
            public WindowsOpticalMediaObservation CheckMedia() => new(owner.Present);
            public WindowsStorageNumber? ReadStorageNumber(out WindowsDeviceError? error) { error = null; return new(2, 1); }
            public WindowsDeviceError? Lock() => throw new Xunit.Sdk.XunitException("Confirmation must not lock media");
            public WindowsDeviceError? Eject() => throw new Xunit.Sdk.XunitException("Confirmation must not eject media");
            public void Dispose() => owner.Active--;
        }
    }
}
