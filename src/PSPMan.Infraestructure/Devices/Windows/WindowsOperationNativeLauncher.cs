using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

internal sealed record WindowsOperationLaunchSpec(string FileName, string Arguments, string Directory, string Verb, uint Mask, int Show);

/// <summary>Fixed PSPMan apphost launch only; no shell interpreter, PATH lookup, arbitrary executable or credentials.</summary>
internal sealed class WindowsOperationNativeLauncher : IWindowsOperationLauncher
{
    public Task<IWindowsOperationProcess> LaunchAsync(string pipeName, Guid requestId,
        WindowsOperationProcessIdentity initiator, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("UAC authorization requires Windows.");
        var spec = BuildSpec(pipeName, requestId, initiator);
        return WindowsOperationLaunchWorker.RunAsync(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            return LaunchNative(spec, initiator);
        }, cancellationToken);
    }

    internal static WindowsOperationLaunchSpec BuildSpec(string pipeName, Guid requestId, WindowsOperationProcessIdentity initiator)
    {
        WindowsOperationIpc.ValidatePipeName(pipeName);
        var path = initiator.ImagePath;
        var file = path.Split('\\', '/').LastOrDefault();
        if (requestId == Guid.Empty || initiator.ProcessId == 0 || initiator.CreationTime <= 0 || initiator.Elevated
            || path.Length is < 7 or > 32767 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.Any(character => char.IsControl(character) || character == '"' || character == '/')
            || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || string.Equals(file, "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A dedicated local apphost and valid initiating process are required.");
        // Only bounded bootstrap identifiers go on the command line. Device targets travel through authenticated IPC.
        var arguments = string.Create(CultureInfo.InvariantCulture,
            $"--device-operation --pipe {pipeName} --request {requestId:D} --initiator {initiator.ProcessId} --initiator-start {initiator.CreationTime}");
        return new(path, arguments, path[..(path.LastIndexOf('\\') + 1)], "runas", 0x540, 0);
    }

    [SupportedOSPlatform("windows")]
    private static IWindowsOperationProcess LaunchNative(WindowsOperationLaunchSpec spec, WindowsOperationProcessIdentity initiator)
    {
        using var own = WindowsOperationNativeProcess.Capture((uint)Environment.ProcessId);
        if (own.Identity != initiator || own.Identity.Elevated)
            throw new UnauthorizedAccessException("The initiating process evidence does not match this process.");
        var initialized = CoInitializeEx(IntPtr.Zero, 0x6); // APARTMENTTHREADED | DISABLE_OLE1DDE on this worker only.
        Marshal.ThrowExceptionForHR(initialized);
        try
        {
            var info = new ShellExecuteInfo
            {
                Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = spec.Mask, Verb = spec.Verb,
                File = spec.FileName, Parameters = spec.Arguments, Directory = spec.Directory, Show = spec.Show
            };
            var succeeded = ShellExecuteExW(ref info);
            var error = Marshal.GetLastPInvokeError();
            using var launched = new SafeProcessHandle(info.Process, ownsHandle: true);
            if (!succeeded) throw new Win32Exception(error);
            if (launched.IsInvalid) throw new IOException("The operation launch returned no process handle.");
            // Pin exactly the returned process object. Never reopen a PID supplied by IPC or search for a process by name.
            return WindowsOperationNativeProcess.Capture(launched);
        }
        finally { CoUninitialize(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        internal int Size;
        internal uint Mask;
        internal IntPtr Window;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? File;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Directory;
        internal int Show;
        internal IntPtr Instance;
        internal IntPtr IdList;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Class;
        internal IntPtr ClassKey;
        internal uint HotKey;
        internal IntPtr IconOrMonitor;
        internal IntPtr Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint options);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}

/// <summary>
/// The OS-owned UAC dialogue cannot be programmatically cancelled here. Cancel the wait and close IPC;
/// retain the global prompt slot until the native call returns, then dispose any late process handle.
/// </summary>
internal static class WindowsOperationLaunchWorker
{
    private static readonly SemaphoreSlim Prompt = new(1, 1);

    internal static async Task<IWindowsOperationProcess> RunAsync(Func<IWindowsOperationProcess> launch, CancellationToken cancellationToken)
    {
        await Prompt.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource<IWindowsOperationProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var worker = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var process = launch();
                if (!completion.TrySetResult(process)) process.Dispose();
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { Prompt.Release(); }
        }) { IsBackground = true, Name = "PSPMan authorization" };
        try
        {
            if (OperatingSystem.IsWindows()) worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
        catch { Prompt.Release(); throw; }
        return await completion.Task.ConfigureAwait(false);
    }
}
