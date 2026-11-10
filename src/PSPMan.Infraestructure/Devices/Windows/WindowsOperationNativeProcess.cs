using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Read-only process evidence held by a noninheritable handle, preventing PID-reuse substitution.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsOperationNativeProcess : IWindowsOperationProcess
{
    private readonly SafeProcessHandle process;
    public WindowsOperationProcessIdentity Identity { get; }
    internal string LogonSid { get; }
    public bool IsAlive => !process.IsClosed && !process.IsInvalid && WaitForSingleObject(process, 0) == 258;

    private WindowsOperationNativeProcess(SafeProcessHandle process)
    {
        this.process = process;
        try
        {
            var id = GetProcessId(process);
            if (id == 0 || !GetProcessTimes(process, out var created, out _, out _, out _)) throw Error();
            var path = new StringBuilder(32768);
            var length = (uint)path.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, path, ref length)) throw Error();
            if (!OpenProcessToken(process, 8, out var token)) { var error = Error(); token?.Dispose(); throw error; }
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                var user = identity.User?.Value ?? throw new UnauthorizedAccessException("Missing process user SID.");
                LogonSid = identity.Groups?.Select(group => group.Value)
                    .SingleOrDefault(sid => sid.StartsWith("S-1-5-5-", StringComparison.Ordinal))
                    ?? throw new UnauthorizedAccessException("Missing process logon SID.");
                var session = TokenUInt(token, 12); // TokenSessionId.
                var elevated = TokenUInt(token, 20) != 0; // TokenElevation, not group membership.
                Identity = new(id, created, path.ToString(), user, session, elevated);
            }
            if (!IsAlive) throw new UnauthorizedAccessException("The expected operation process has exited.");
        }
        catch { process.Dispose(); throw; }
    }

    internal static WindowsOperationNativeProcess Capture(uint processId)
    {
        var handle = OpenProcess(0x00101000, false, processId); // SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION.
        if (handle.IsInvalid) { var error = Error(); handle.Dispose(); throw error; }
        return new(handle);
    }

    /// <summary>T34 must use the ShellExecuteEx process handle, never a PID claimed by the IPC peer.</summary>
    internal static WindowsOperationNativeProcess Capture(SafeProcessHandle launchHandle)
    {
        if (!DuplicateHandle(GetCurrentProcess(), launchHandle, GetCurrentProcess(), out var pinned, 0, false, 2))
        { var error = Error(); pinned?.Dispose(); throw error; }
        return new(pinned);
    }

    private static uint TokenUInt(SafeAccessTokenHandle token, int informationClass)
    {
        if (!GetTokenInformation(token, informationClass, out var value, sizeof(uint), out var size) || size != sizeof(uint)) throw Error();
        return value;
    }

    public void Dispose() => process.Dispose();
    private static Win32Exception Error() => new(Marshal.GetLastPInvokeError());

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeProcessHandle source, IntPtr targetProcess,
        out SafeProcessHandle duplicate, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out uint value, uint size, out uint required);
}
