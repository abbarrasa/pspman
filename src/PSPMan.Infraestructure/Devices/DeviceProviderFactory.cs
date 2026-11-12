using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Infraestructure.Devices.Linux;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Devices;

public enum DevicePlatform { Unsupported, Linux, Windows }

/// <summary>Successful composition transfers ownership of every provider to the session.</summary>
public sealed record DeviceProviderFactoryResult(ImmutableArray<IDeviceProvider> Providers, DeviceOperationError? Error = null);

public static class DeviceProviderFactory
{
    public static DevicePlatform CurrentPlatform => OperatingSystem.IsLinux() ? DevicePlatform.Linux
        : OperatingSystem.IsWindows() ? DevicePlatform.Windows : DevicePlatform.Unsupported;

    public static ValueTask<DeviceProviderFactoryResult> CreateAsync(CancellationToken cancellationToken = default) =>
        CreateAsync(CurrentPlatform, LinuxSessionDeviceProvider.CreateAsync,
            token => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<IDeviceProvider>(new WindowsSessionDeviceProvider()); }, cancellationToken);

    /// <summary>Inject constructors to verify selection without loading foreign native libraries.</summary>
    public static async ValueTask<DeviceProviderFactoryResult> CreateAsync(DevicePlatform platform,
        Func<CancellationToken, ValueTask<IDeviceProvider>> createLinux,
        Func<CancellationToken, ValueTask<IDeviceProvider>> createWindows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (platform is not (DevicePlatform.Linux or DevicePlatform.Windows))
            return new([], new("UnsupportedPlatform", "Device services are supported on Linux and Windows only."));
        IDeviceProvider? provider = null;
        try
        {
            provider = await (platform == DevicePlatform.Linux ? createLinux : createWindows)(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return FromProviders([provider]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (provider is not null) await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            if (provider is not null) await provider.DisposeAsync().ConfigureAwait(false);
            return new([], new("DeviceServiceUnavailable", "Device services could not be initialized."));
        }
    }

    /// <summary>Validates an explicit provider list without starting discovery. Ownership transfers on success only.</summary>
    public static DeviceProviderFactoryResult FromProviders(IEnumerable<IDeviceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var items = providers.ToImmutableArray();
        if (items.Any(provider => provider is null || string.IsNullOrWhiteSpace(provider.ProviderId))
            || items.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Device providers must have distinct nonempty identifiers.", nameof(providers));
        return new(items);
    }
}
