using Avalonia.Headless;
using Avalonia.Threading;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class HeadlessTestDispatcherTests
{
    [Fact]
    public async Task DispatchWaitsForTheCallbackAfterAnAsynchronousSuspension()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var completed = false;
        await session.DispatchAsync(async () =>
        {
            await Task.Delay(100);
            Assert.True(Dispatcher.UIThread.CheckAccess());
            completed = true;
        });
        Assert.True(completed);
    }

    [Fact]
    public async Task DispatchPropagatesExceptionsAfterAnAsynchronousSuspension()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        var failure = new InvalidOperationException("Failure after suspension");
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => session.DispatchAsync(async () =>
        {
            await Task.Yield();
            throw failure;
        }));
        Assert.Same(failure, observed);
    }
}
