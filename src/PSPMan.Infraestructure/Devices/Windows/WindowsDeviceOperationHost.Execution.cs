using System.ComponentModel;
using System.Text;

namespace PSPMan.Infraestructure.Devices.Windows;

public sealed partial class WindowsDeviceOperationHost
{
    private readonly IWindowsDeviceApi inventory;
    private readonly IWindowsVolumeMountApi mount;
    private readonly IWindowsRemovalNativeApi removal;
    private readonly IWindowsOpticalNativeApi optical;
    private readonly bool requiresWindows;
    private int executed;

    public WindowsDeviceOperationHost() : this(new WindowsDeviceApi(), new WindowsVolumeMountApi(),
        new WindowsRemovalNativeApi(), new WindowsOpticalNativeApi()) => requiresWindows = true;

    internal WindowsDeviceOperationHost(IWindowsDeviceApi inventory, IWindowsVolumeMountApi mount,
        IWindowsRemovalNativeApi removal, IWindowsOpticalNativeApi optical)
    {
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.mount = mount ?? throw new ArgumentNullException(nameof(mount));
        this.removal = removal ?? throw new ArgumentNullException(nameof(removal));
        this.optical = optical ?? throw new ArgumentNullException(nameof(optical));
    }

    /// <summary>For the authenticated T32 responder only. One host instance consumes one attempt, even on failure.</summary>
    internal async Task<WindowsOperationReply> ExecuteAsync(WindowsOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref executed, 1) != 0) throw new InvalidOperationException("An operation host is single-use.");
        WindowsDeviceAuthorization.ValidateRequest(request);
        if (requiresWindows && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows operations require Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var baseline = await inventory.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var target = ValidateTarget(request, baseline);
            if (!target.IsValid) return Reply(request, WindowsDeviceRemovalFailure.FromError(target.Error!));

            // Each operation service must compare its own first fresh observation to the host selection.
            // Removal also has a separate native preparation entry; guard that observation independently.
            // Later inventory is actual post-operation state, NOT required to equal the pre-operation state.
            IWindowsDeviceApi GuardedInventory() => new SelectionInventory(inventory, baseline, request);
            switch (request.Operation)
            {
                case WindowsElevatedOperation.MountVolume:
                    var mounted = await new WindowsVolumeMountService(GuardedInventory(), mount)
                        .MountAsync(target.MountTarget!, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Reply(request, mounted.Succeeded ? null : WindowsDeviceRemovalFailure.FromError(
                        mounted.Error ?? new("MountNotObserved", "MountVolume")) with { VolumeGuidPath = request.VolumeGuidPath });
                case WindowsElevatedOperation.RemoveDevice:
                    var removed = await new WindowsDeviceRemovalService(GuardedInventory(),
                        new WindowsDeviceRemovalApi(GuardedInventory(), removal))
                        .RemoveAsync(target.RemovalTarget!, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Reply(request, removed.Succeeded && removed.Failure is null ? null
                        : removed.Failure ?? new(new("RemovalIncomplete", "RemoveDevice")));
                case WindowsElevatedOperation.EjectOpticalMedia:
                    var ejected = await new WindowsOpticalMediaEjector(GuardedInventory(), optical)
                        .EjectAsync(target.OpticalTarget!, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Reply(request, ejected.Succeeded && ejected.Failure is null ? null
                        : ejected.Failure ?? new(new("EjectionIncomplete", "EjectOpticalMedia")));
                default:
                    throw WindowsOperationProtocol.Invalid();
            }
        }
        catch (SelectionChangedException error) { return Reply(request, WindowsDeviceRemovalFailure.FromError(error.Error)); }
        catch (Win32Exception error)
        {
            // Native code is useful; arbitrary exception text can contain unrelated paths or caller data.
            return Reply(request, new(new("NativeFailure", "ExecuteOperation", error.NativeErrorCode)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Reply(request, new(new("OperationFailed", "ExecuteOperation", error is UnauthorizedAccessException ? 5 : null)));
        }
    }

    private static WindowsOperationReply Reply(WindowsOperationRequest request, WindowsDeviceRemovalFailure? failure)
    {
        if (failure is not null)
        {
            failure = failure with
            {
                Error = failure.Error with
                {
                    Code = DiagnosticToken(failure.Error.Code, "NativeFailure"),
                    Operation = DiagnosticToken(failure.Error.Operation, "ExecuteOperation"),
                    Message = DiagnosticText(failure.Error.Message, 2048)
                },
                VetoName = DiagnosticText(failure.VetoName, 260),
                VolumeGuidPath = failure.VolumeGuidPath is { } guid && WindowsDeviceApi.IsVolumeGuidPath(guid) ? guid : null
            };
        }
        var reply = new WindowsOperationReply(WindowsDeviceAuthorization.ProtocolVersion, request.RequestId,
            request.Operation, failure is null, failure);
        WindowsDeviceAuthorization.EncodeReply(reply); // Enforce the wire contract before handing the result to IPC.
        return reply;
    }

    private static string DiagnosticToken(string value, string fallback)
    {
        try { WindowsOperationProtocol.Token(value); return value; }
        catch (InvalidDataException) { return fallback; }
    }

    private static string? DiagnosticText(string? value, int maximum)
    {
        if (value is null) return null;
        var bounded = new StringBuilder(Math.Min(value.Length, maximum));
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune) && rune.Value is not ('\r' or '\n' or '\t')) continue;
            if (bounded.Length + rune.Utf16SequenceLength > maximum) break;
            bounded.Append(rune.ToString());
        }
        return bounded.ToString();
    }

    private sealed class SelectionChangedException(WindowsDeviceError error) : Exception("The selected operation target changed.")
    {
        internal WindowsDeviceError Error => error;
    }

    /// <summary>Guard only the selection entry. Existing services still own native locking and post-action confirmation.</summary>
    private sealed class SelectionInventory(IWindowsDeviceApi source, WindowsStorageSnapshot baseline,
        WindowsOperationRequest request) : IWindowsDeviceApi
    {
        private int observed;
        public async ValueTask<WindowsStorageSnapshot> EnumerateStorageAsync(CancellationToken cancellationToken = default)
        {
            var first = Interlocked.Exchange(ref observed, 1) == 0;
            var snapshot = await source.EnumerateStorageAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (first)
            {
                var validation = ValidateTarget(request, snapshot);
                if (!validation.IsValid) throw new SelectionChangedException(validation.Error!);
                if (!SameSelection(baseline, snapshot, request.DeviceInstanceId))
                    throw new SelectionChangedException(new("TargetChanged", "ValidateOperationTarget"));
            }
            return snapshot;
        }
    }

    private static bool SameSelection(WindowsStorageSnapshot before, WindowsStorageSnapshot after, string instanceId)
    {
        WindowsStorageDevice Device(WindowsStorageSnapshot snapshot) => snapshot.Devices.Single(device =>
            string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        static string? Normalize(string? value) => value?.ToUpperInvariant();
        static object DeviceKey(WindowsStorageDevice device) => (Normalize(device.InterfacePath), Normalize(device.ParentInstanceId),
            device.StorageNumber, device.Bus, device.IsOptical, device.IsRemovable, device.CanEject);
        static IEnumerable<object> Volumes(WindowsStorageSnapshot snapshot, WindowsStorageDevice device) =>
            device.StorageNumber is { } number ? snapshot.Volumes.Where(volume => volume.StorageNumber == number)
                .OrderBy(volume => volume.VolumeGuidPath ?? volume.InterfacePath, StringComparer.OrdinalIgnoreCase)
                .Select(volume => (object)(Normalize(volume.InterfacePath), Normalize(volume.VolumeGuidPath),
                    volume.StorageNumber, volume.FileSystemSerialNumber, volume.State)) : [];
        var previous = Device(before);
        var current = Device(after);
        return Equals(DeviceKey(previous), DeviceKey(current)) && Volumes(before, previous).SequenceEqual(Volumes(after, current));
    }
}
