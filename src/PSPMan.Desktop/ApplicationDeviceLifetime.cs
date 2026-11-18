using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Desktop.ViewModels;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Desktop;

/// <summary>UI-context owner of one execution's device services and controlled asynchronous shutdown.</summary>
public sealed class ApplicationDeviceLifetime : IAsyncDisposable
{
    private readonly Window _window;
    private readonly MainViewModel _main;
    private readonly Action<int> _shutdown;
    private readonly Func<CancellationToken, ValueTask<DeviceProviderFactoryResult>> _createProviders;
    private readonly Func<DeviceSessionService?, string?, CopyStartup> _createCopy;
    private readonly CancellationTokenSource _cancellation = new();
    private DeviceStartup? _startup;
    private CopyStartup? _copy;
    private DevicesViewModel _devices;
    private Task? _disposeTask;
    private bool _started, _stopping, _shutdownRequested, _presentationDetached;

    public ApplicationDeviceLifetime(Window window, MainViewModel main, Action<int> shutdown,
        Func<CancellationToken, ValueTask<DeviceProviderFactoryResult>>? createProviders = null,
        Func<DeviceSessionService?, string?, CopyStartup>? createCopy = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _window = window;
        _main = main;
        _shutdown = shutdown;
        _createProviders = createProviders ?? DeviceProviderFactory.CreateAsync;
        _createCopy = createCopy ?? ((session, error) => new(session, error));
        _devices = new(initializing: true);
        _main.AttachDevices(_devices);
        _window.Opened += OnOpened;
        _window.Closing += OnClosing;
    }

    public Task Initialization { get; private set; } = Task.CompletedTask;
    public Task ShutdownCompletion { get; private set; } = Task.CompletedTask;

    private void OnOpened(object? sender, EventArgs args)
    {
        if (_started || _stopping) return;
        _started = true;
        // Reserve the lifetime barrier before any factory/UI observer can reenter disposal.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Initialization = completion.Task;
        _ = InitializeAndCompleteAsync(completion);
    }

    private async Task InitializeAndCompleteAsync(TaskCompletionSource completion)
    {
        try { await InitializeAsync(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Provider constructors may contain synchronous native work before their first await.
            _startup = await Task.Run(async () => await DeviceStartup.CreateAsync(
                createProviders: _createProviders, cancellationToken: _cancellation.Token).ConfigureAwait(false));
            if (_stopping) return;
            _copy = _createCopy(_startup.Session, _startup.Error?.Message);
            if (_stopping) return;
            _main.AttachCopy(_copy.ViewModel);
            if (_stopping) return;
            ReplaceDevices(new(_startup.Session, startupError: _startup.Error, removalInterceptor: _copy.Coordinator));
            if (_stopping) return;
            await _startup.StartAsync(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_stopping)
                ReplaceDevices(new(startupError: new DeviceOperationError("DeviceServiceUnavailable", "Device services could not be initialized.")));
        }
    }

    private void ReplaceDevices(DevicesViewModel devices)
    {
        _devices.Dispose();
        _devices = devices;
        _main.AttachDevices(devices);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (!args.Cancel && RequestShutdown()) args.Cancel = true;
    }

    /// <summary>Returns whether the original shutdown must be cancelled while cleanup finishes.</summary>
    public bool RequestShutdown()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_shutdownRequested) return true;
        if (_disposeTask?.IsCompleted == true) return false;
        _shutdownRequested = true;
        ShutdownCompletion = ShutdownAsync();
        return true;
    }

    private async Task ShutdownAsync()
    {
        var exitCode = 0;
        try
        {
            if (_copy is { } copy)
            {
                var confirmed = await copy.ViewModel.ConfirmCloseAsync();
                if (!confirmed && !_stopping)
                {
                    _shutdownRequested = false;
                    return;
                }
                if (!_stopping) await copy.ViewModel.StopForShutdownAsync();
            }
        }
        catch (Exception) { exitCode = 1; }
        try
        {
            // A failed stop must not bypass device/provider cleanup.
            var cleanup = DisposeAsync();
            // Unwind Closing/ShutdownRequested before requesting the final application exit.
            await Task.Yield();
            await cleanup;
        }
        catch (Exception) { exitCode = 1; }
        _shutdown(exitCode);
    }

    public ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposeTask is not null) return new(_disposeTask);
        // UI notifications and cancellation callbacks may request disposal again.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _disposeTask = completion.Task;
        _stopping = true;
        _ = DisposeAndCompleteAsync(completion);
        return new(completion.Task);
    }

    private async Task DisposeAndCompleteAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                try
                {
                    // No copy owner exists during early startup: retire initializing UI immediately.
                    if (_copy is null) DetachPresentation();
                }
                finally { _cancellation.Cancel(); }
            }
            finally
            {
                await DisposeCoreAsync();
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            // A late factory result is still owned and must be released before exit.
            try { await Initialization; }
            finally
            {
                try
                {
                    if (_copy is not null) await _copy.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        DetachPresentation();
                    }
                    finally
                    {
                        if (_startup is not null)
                            await Task.Run(async () => await _startup.DisposeAsync().ConfigureAwait(false));
                    }
                }
            }
        }
        finally
        {
            _window.Opened -= OnOpened;
            _window.Closing -= OnClosing;
            _cancellation.Dispose();
        }
    }

    private void DetachPresentation()
    {
        if (_presentationDetached) return;
        _presentationDetached = true;
        try { _devices.Dispose(); }
        finally
        {
            try { _main.AttachDevices(null); }
            finally { _main.AttachCopy(null); }
        }
    }
}
