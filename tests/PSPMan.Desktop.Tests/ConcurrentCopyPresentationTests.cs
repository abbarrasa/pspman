using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed class ConcurrentCopyPresentationTests
{
    [Fact]
    public async Task SimultaneousInputsHaveIndependentOwners()
    {
        await using var model = new GameCopyViewModel(action => action());
        var first = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
        var second = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
        using var stop = new CancellationTokenSource();
        var password = model.RequestPasswordAsync(new(first, GameCopyErrorCode.PasswordRequired), stop.Token).AsTask();
        var overwrite = model.ConfirmOverwriteAsync(new(second, 0, "/psp/ISO/game.iso", new("file", 3, "v1"), "Retained"), stop.Token).AsTask();
        Assert.False(password.IsCompleted);
        Assert.False(overwrite.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => password);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => overwrite);
    }
}
