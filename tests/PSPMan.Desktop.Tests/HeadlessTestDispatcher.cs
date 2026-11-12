using Avalonia.Headless;

namespace PSPMan.Desktop.Tests;

internal static class HeadlessTestDispatcher
{
    // Avalonia 12 has no Dispatch(Func<Task>) overload: inference can yield Task<Task>.
    public static Task DispatchAsync(this HeadlessUnitTestSession session, Func<Task> action,
        CancellationToken cancellationToken = default) => session.Dispatch<bool>(async () =>
    {
        await action();
        return true;
    }, cancellationToken);
}
