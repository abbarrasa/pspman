using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices;

namespace PSPMan.Desktop;

/// <summary>Device composition has no dependency on catalog initialization or Avalonia lifetime.</summary>
public sealed class DeviceStartup : IAsyncDisposable
{
    private DeviceStartup(DeviceSessionService? session, DeviceOperationError? error) { Session = session; Error = error; }
    public DeviceSessionService? Session { get; }
    public DeviceOperationError? Error { get; }

    public static async ValueTask<DeviceStartup> CreateAsync(IEnumerable<IDeviceProvider>? providers = null,
        Func<CancellationToken, ValueTask<DeviceProviderFactoryResult>>? createProviders = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Invalid explicit lists are caller errors and retain caller ownership.
        DeviceProviderFactoryResult composition;
        if (providers is not null) composition = DeviceProviderFactory.FromProviders(providers);
        else
        {
            try { composition = await (createProviders ?? DeviceProviderFactory.CreateAsync)(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return new(null, new("DeviceServiceUnavailable", "Device services could not be initialized.")); }
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (composition.Error is not null)
            {
                foreach (var provider in composition.Providers) await provider.DisposeAsync().ConfigureAwait(false);
                return new(null, composition.Error);
            }
            return new(new DeviceSessionService(composition.Providers), null);
        }
        catch
        {
            foreach (var provider in composition.Providers) await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Application lifetime wiring and actual discovery activation belong to T44.
    public Task StartAsync(CancellationToken cancellationToken = default) => Session?.StartAsync(cancellationToken) ?? Task.CompletedTask;
    public ValueTask DisposeAsync() => Session?.DisposeAsync() ?? ValueTask.CompletedTask;
}
