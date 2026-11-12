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
    private readonly CancellationTokenSource _cancellation = new();
    private DeviceStartup? _startup;
    private DevicesViewModel _devices;
    private Task? _disposeTask;
    private bool _started, _stopping, _shutdownRequested;

    public ApplicationDeviceLifetime(Window window, MainViewModel main, Action<int> shutdown,
        Func<CancellationToken, ValueTask<DeviceProviderFactoryResult>>? createProviders = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _window = window;
        _main = main;
        _shutdown = shutdown;
        _createProviders = createProviders ?? DeviceProviderFactory.CreateAsync;
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
        Initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Provider constructors may contain synchronous native work before their first await.
            _startup = await Task.Run(async () => await DeviceStartup.CreateAsync(
                createProviders: _createProviders, cancellationToken: _cancellation.Token).ConfigureAwait(false));
            if (_stopping) return;
            ReplaceDevices(new(_startup.Session, startupError: _startup.Error));
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
        if (_disposeTask is null)
        {
            _stopping = true;
            _devices.Dispose();
            _main.AttachDevices(null);
            _cancellation.Cancel();
            _disposeTask = DisposeCoreAsync();
        }
        return new(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            // A late factory result is still owned and must be released before exit.
            await Initialization;
            if (_startup is not null)
                await Task.Run(async () => await _startup.DisposeAsync().ConfigureAwait(false));
        }
        finally
        {
            _window.Opened -= OnOpened;
            _window.Closing -= OnClosing;
            _cancellation.Dispose();
        }
    }
}
