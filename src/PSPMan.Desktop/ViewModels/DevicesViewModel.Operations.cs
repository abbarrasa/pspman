using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PSPMan.Core.Devices;

namespace PSPMan.Desktop.ViewModels;

public sealed partial class DevicesViewModel
{
    private readonly HashSet<DeviceKey> _submittingOperations = [];
    public string? DeviceActionError { get; private set; }

    private bool CanOperate(DeviceSessionSnapshot observation, DeviceSnapshot device, VolumeKey? retry)
    {
        lock (_gate)
        {
            if (_disposed || _session is null || _submittingOperations.Contains(device.Key)
                || _session.Snapshot.Inventory.Revision != observation.Inventory.Revision
                || device.State is DeviceState.Detecting or DeviceState.Mounting or DeviceState.Removing or DeviceState.Unavailable)
                return false;
            return retry is { } volume
                ? (device.Capabilities & DeviceCapabilities.Mount) != 0 && observation.RetryableMounts.Contains(volume)
                    && device.Volumes.Any(item => item.Key == volume && item.State == DeviceState.Error && item.Root is null)
                : (device.Capabilities & DeviceCapabilities.SafeRemoval) != 0;
        }
    }

    private DeviceItemViewModel ProjectDevice(DeviceSessionSnapshot observation, DeviceSnapshot device)
    {
        var row = new DeviceItemViewModel(device);
        row.ConfigureRemoval(() => CanOperate(observation, device, null), () => OperateAsync(observation, device, null));
        foreach (var volume in row.Volumes)
        {
            var key = volume.Key;
            volume.ConfigureRetry(() => CanOperate(observation, device, key), () => OperateAsync(observation, device, key));
        }
        return row;
    }

    private async Task OperateAsync(DeviceSessionSnapshot observation, DeviceSnapshot device, VolumeKey? retry)
    {
        lock (_gate)
        {
            // ExecuteAsync can be called directly without checking CanExecute.
            if (!CanOperate(observation, device, retry)) return;
            _submittingOperations.Add(device.Key);
        }
        PostOperationFeedback(null);
        try
        {
            if (retry is { } volume)
                await _session!.RetryMountAsync(volume, _sourceLifetime.Token, observation.Inventory).ConfigureAwait(false);
            else
                await _session!.RemoveDeviceAsync(device.Key, _sourceLifetime.Token, observation.Inventory).ConfigureAwait(false);
            // Acceptance is not native completion. Only session observations supply operation state.
        }
        catch (OperationCanceledException) when (_sourceLifetime.IsCancellationRequested) { }
        catch (Exception exception) { PostOperationFeedback($"Unable to request the device operation: {exception.Message}"); }
        finally
        {
            lock (_gate) _submittingOperations.Remove(device.Key);
            PostOperationRefresh();
        }
    }

    private void PostOperationFeedback(string? error) => _postToUi(() =>
    {
        lock (_gate)
        {
            if (_disposed) return;
            DeviceActionError = error;
            OnPropertyChanged(nameof(DeviceActionError));
            foreach (var row in Devices) row.RefreshCommands();
        }
    });

    private void PostOperationRefresh(bool disposing = false) => _postToUi(() =>
    {
        lock (_gate)
        {
            if (_disposed && !disposing) return;
            foreach (var row in Devices) row.RefreshCommands();
        }
    });
}
