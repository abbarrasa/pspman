using System.Globalization;

namespace PSPMan.Infraestructure.Devices.Windows;

internal sealed record WindowsOperationBootstrap(string PipeName, Guid RequestId, uint InitiatorProcessId, long InitiatorCreationTime);

/// <summary>A fixed early process entry point. It never initializes UI, catalog services or a general command runner.</summary>
public static class WindowsDeviceOperationMode
{
    private static readonly string[] Fields = ["--pipe", "--request", "--initiator", "--initiator-start"];

    /// <summary>Null means ordinary startup; every recognized helper invocation terminates with an exit status.</summary>
    public static int? RunIfRequested(string[] args) => RunIfRequested(args, bootstrap =>
    {
        if (!OperatingSystem.IsWindows()) return 2;
        return RunBootstrapAsync(bootstrap, (uint)Environment.ProcessId, CaptureProcess,
            (request, initiating, token) => new WindowsDeviceOperationHost().RunAsync(request.PipeName,
                request.RequestId, initiating, TimeSpan.FromMinutes(2), token), CancellationToken.None).GetAwaiter().GetResult();
    });

    internal static int? RunIfRequested(string[] args, Func<WindowsOperationBootstrap, int> run)
    {
        if (args is null) return 2;
        if (!args.Any(IsHelperArgument)) return null;
        try
        {
            var result = run(Parse(args));
            return result is 0 or 1 ? result : 2;
        }
        catch (Exception)
        {
            // This is a process boundary, not a fallback opportunity. No untrusted argument/exception
            // text is printed and no helper failure is allowed to continue into ordinary UI startup.
            return 2;
        }
    }

    private static bool IsHelperArgument(string? argument)
    {
        if (string.IsNullOrEmpty(argument) || argument[0] is not ('-' or '/')) return false;
        var name = argument.TrimStart('-', '/');
        return name.StartsWith("device-operation", StringComparison.OrdinalIgnoreCase)
            || Fields.Any(field => name.StartsWith(field[2..], StringComparison.OrdinalIgnoreCase));
    }

    internal static WindowsOperationBootstrap Parse(string[] args)
    {
        if (args.Length != 9 || args.Any(value => value is null || value.Length > 128) || args[0] != "--device-operation")
            throw WindowsOperationProtocol.Invalid();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
            if (!Fields.Contains(args[index], StringComparer.Ordinal) || !fields.TryAdd(args[index], args[index + 1]))
                throw WindowsOperationProtocol.Invalid();
        if (fields.Count != Fields.Length) throw WindowsOperationProtocol.Invalid();
        var pipe = fields["--pipe"];
        WindowsOperationIpc.ValidatePipeName(pipe);
        if (fields["--request"].Length != 36 || !Guid.TryParseExact(fields["--request"], "D", out var requestId) || requestId == Guid.Empty
            || !uint.TryParse(fields["--initiator"], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId == 0
            || fields["--initiator"] != processId.ToString(CultureInfo.InvariantCulture)
            || !long.TryParse(fields["--initiator-start"], NumberStyles.None, CultureInfo.InvariantCulture, out var created) || created <= 0
            || fields["--initiator-start"] != created.ToString(CultureInfo.InvariantCulture))
            throw WindowsOperationProtocol.Invalid();
        return new(pipe, requestId, processId, created);
    }

    private static IWindowsOperationProcess CaptureProcess(uint processId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return WindowsOperationNativeProcess.Capture(processId);
    }

    internal static async Task<int> RunBootstrapAsync(WindowsOperationBootstrap bootstrap, uint currentProcessId,
        Func<uint, IWindowsOperationProcess> capture,
        Func<WindowsOperationBootstrap, IWindowsOperationProcess, CancellationToken, Task<int>> run,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        deadline.Token.ThrowIfCancellationRequested();
        using var local = capture(currentProcessId);
        using var initiating = capture(bootstrap.InitiatorProcessId);
        var own = local.Identity;
        var expected = initiating.Identity;
        if (!local.IsAlive || !initiating.IsAlive || !own.Elevated || expected.Elevated
            || own.ProcessId != currentProcessId || own.ProcessId == expected.ProcessId
            || expected.ProcessId != bootstrap.InitiatorProcessId || expected.CreationTime != bootstrap.InitiatorCreationTime
            || own.SessionId != expected.SessionId || string.IsNullOrWhiteSpace(own.UserSid) || string.IsNullOrWhiteSpace(expected.UserSid)
            || !string.Equals(own.ImagePath, expected.ImagePath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The operation bootstrap process could not be verified.");
        try { WindowsOperationNativeLauncher.BuildSpec(bootstrap.PipeName, bootstrap.RequestId, expected); }
        catch (ArgumentException) { throw new UnauthorizedAccessException("A verified dedicated apphost is required."); }
        deadline.Token.ThrowIfCancellationRequested();
        // T32 checks that the actual pipe server is this still-pinned initiating process before reading a request.
        return await run(bootstrap, initiating, deadline.Token).ConfigureAwait(false);
    }
}
