using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class GameCopyPresentationTests
{
    [Fact]
    public async Task PasswordIsMaskedAndReleasedAfterRetry()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            await using var model = new GameCopyViewModel(action => action());
            var view = new GameCopyView { DataContext = model };
            var window = new Window { Content = view, Width = 900, Height = 450 };
            window.Show();
            var identity = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
            var response = model.RequestPasswordAsync(new(identity, GameCopyErrorCode.IndeterminateEncryptedError), default);
            Dispatcher.UIThread.RunJobs();
            var prompt = Assert.IsType<CopyInputViewModel>(model.Input);
            Assert.Contains("Incorrect password or damaged archive", prompt.Message);
            var input = view.CopyControl<TextBox>("CopyPasswordInput")!;
            Assert.Equal('●', input.PasswordChar);
            input.Text = "private-password";
            var retry = view.CopyControl<Button>("RetryCopyPasswordButton")!;
            Assert.Same(prompt.RetryCommand, retry.Command);
            Assert.True(retry.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("private-password", (await response).Password);
            Assert.Empty(prompt.Password);
            Assert.Null(model.Input);
            window.Close();
        });
    }

    [Fact]
    public async Task RetiredPromptCannotAnswerReplacementAndCancellationReleasesInput()
    {
        await using var model = new GameCopyViewModel(action => action());
        using var cancellation = new CancellationTokenSource();
        var first = model.RequestPasswordAsync(new(new(Guid.NewGuid(), Guid.NewGuid()), GameCopyErrorCode.PasswordRequired), cancellation.Token);
        var retired = model.Input!;
        retired.Password = "secret";
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.AsTask());
        Assert.Empty(retired.Password);
        var identity = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
        var second = model.ConfirmOverwriteAsync(new(identity, 1, "/psp/ISO/game.iso", new("file", 2, "v2"), "Overwritten files are not restored."), default);
        retired.RetryCommand.Execute(null);
        Assert.False(second.IsCompleted);
        model.Input!.SkipCommand.Execute(null);
        Assert.Equal(CopyOverwriteDecision.Skip, (await second).Decision);
        Assert.Null(model.Input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsEitherPromptAndIgnoresQueuedPublication(bool password)
    {
        var queued = new Queue<Action>();
        var model = new GameCopyViewModel(queued.Enqueue);
        var identity = new GameCopyPrompt(Guid.NewGuid(), Guid.NewGuid());
        Task pending = password
            ? model.RequestPasswordAsync(new(identity, GameCopyErrorCode.IncorrectPassword), default).AsTask()
            : model.ConfirmOverwriteAsync(new(identity, 0, "/psp/ISO/a", new("a", 1, "a"), "retained"), default).AsTask();
        await model.DisposeAsync();
        while (queued.TryDequeue(out var action)) action();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(model.Input);
    }

    [Fact]
    public async Task ViewLocatorResolvesCopyViewAndDisposedInputCannotRestartWork()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var model = new GameCopyViewModel(action => action());
            Assert.IsType<GameCopyView>(new ViewLocator().Build(model));
            var pending = model.RequestPasswordAsync(new(new(Guid.NewGuid(), Guid.NewGuid()), GameCopyErrorCode.PasswordRequired), default);
            var input = model.Input!;
            input.Password = "private";
            await model.DisposeAsync();
            await model.DisposeAsync();
            input.RetryCommand.Execute(null);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.AsTask());
            Assert.Empty(input.Password); Assert.Null(model.Input);
            await model.StartAsync(); Assert.False(model.IsOpen);
        });
    }
}
