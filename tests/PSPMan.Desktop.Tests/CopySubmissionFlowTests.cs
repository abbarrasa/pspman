using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using static PSPMan.Desktop.Tests.GameCopyTestContext;
using static PSPMan.Desktop.Tests.CopySubmissionDraftTests;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class CopySubmissionFlowTests
{
    private static async Task<(GameCopyTestContext Context, MutableRepository Repository)> Batch(GameRecord[] games, int psps = 1, DeviceInventorySnapshot? inventory = null)
    {
        var f = await Create(psps, action => Dispatcher.UIThread.Post(action), inventory);
        f.Main.AttachCopy(null); f.Main.AttachDevices(null);
        var repository = new MutableRepository(games);
        f.Main = new(new GameCatalogService(repository), null, devicesViewModel: f.DeviceModel, mountedPathSyntax: MountedPathSyntax.Linux);
        f.Main.AttachCopy(f.Copy); f.Main.EnableCopyPreparation(); f.Main.SelectedGame = games[0];
        await Wait(() => f.DeviceModel.SubmissionObservation is not null);
        return (f, repository);
    }
    private static void Select(GameCopyTestContext f, params GameRecord[] games)
    { f.Main.SelectedGames.Clear(); foreach (var game in games) f.Main.SelectedGames.Add(game); f.Main.PrepareSelectedCopiesCommand.Execute(null); }
    private static void Hold(GameCopyTestContext f) => f.IO.Pause = token => Task.Delay(Timeout.Infinite, token);

    [Fact]
    public async Task MixedReadyBatchReturnsPromptlyAndRequiresExplicitResubmission()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var first = Game(); var second = Game() with { Location = "/second.zip" };
            var (f, repository) = await Batch([first, second]); await using var context = f;
            Hold(f); Select(f, first, second); Assert.Equal(0, f.IO.Opens);
            f.Main.CopyDrafts[1].SelectedPsp = null;
            f.Main.SendPreparedCopiesCommand.Execute(null);
            var admission = f.Main.LastCopyAdmission!;
            Assert.Single(admission.Accepted); Assert.Equal(first.Id, admission.Accepted[0].Game.Id);
            Assert.Equal(1, Assert.Single(admission.Incidents).SubmissionIndex);
            Assert.Single(f.Main.CopyDrafts); Assert.Equal(second.Id, f.Main.CopyDrafts[0].Game.Id);
            await Wait(() => f.Copy.Operations.Count == 1 && f.Copy.Operations[0].Status == "Transferring");
            Assert.False(f.Copy.GetCompletion(admission.Accepted[0].OperationId).IsCompleted);
            Assert.True(f.Main.CanPrepareSelectedCopies); Assert.True(f.Main.SendPreparedCopiesCommand.CanExecute(null));
            var draft = f.Main.CopyDrafts[0]; draft.SelectedPsp = draft.Psps[0];
            Assert.True(draft.IsReady); Assert.Equal(1, f.IO.Opens); Assert.Single(f.Copy.Operations);
            f.Main.SendPreparedCopiesCommand.Execute(null);
            Assert.Single(f.Main.LastCopyAdmission!.Accepted); Assert.Empty(f.Main.CopyDrafts);
            await Wait(() => f.Copy.Operations.Count == 2 && f.Copy.Operations[1].Input is { IsOverwrite: true });
            f.Copy.Operations[1].Input!.OverwriteCommand.Execute(null);
            await Wait(() => f.Copy.Operations.All(row => row.Status == "Transferring"));
            Assert.Equal(0, repository.Writes); Assert.Equal(2, f.IO.Opens);
            await f.Startup.Coordinator!.CancelAllAsync();
        });
    }

    [Fact]
    public async Task SameGameCanBeSentAgainToAnotherPspAndSelectionCannotRetargetEitherOperation()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var game = Game(); var (f, repository) = await Batch([game], psps: 2); await using var context = f;
            Hold(f); var preferences = f.Session.Snapshot.Associations;
            Select(f, game); var draft = f.Main.CopyDrafts[0]; draft.SelectedPsp = draft.Psps[0];
            f.Main.SendPreparedCopiesCommand.Execute(null); var first = f.Main.LastCopyAdmission!.Accepted.Single();
            await Wait(() => f.Copy.Operations.Count == 1 && f.Copy.Operations[0].Status == "Transferring");
            Select(f, game); var next = f.Main.CopyDrafts[0]; next.SelectedPsp = next.Psps[1];
            f.Main.SendPreparedCopiesCommand.Execute(null); var second = f.Main.LastCopyAdmission!.Accepted.Single();
            Assert.NotEqual(first.OperationId, second.OperationId); Assert.NotEqual(first.Destination.Device, second.Destination.Device);
            f.Main.SelectedGames.Clear(); f.Main.SelectedGame = null;
            await Wait(() => f.Copy.Operations.Count == 2);
            Assert.Equal(first, f.Copy.Operations.Single(row => row.OperationId == first.OperationId).Request);
            Assert.Equal(second, f.Copy.Operations.Single(row => row.OperationId == second.OperationId).Request);
            Assert.Same(preferences, f.Session.Snapshot.Associations); Assert.Equal(0, repository.Writes);
            await f.Startup.Coordinator!.CancelAllAsync();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DvdDraftsCanShareOneReaderOrChooseDifferentMountedReaders(bool separate)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var first = Game(GameMedium.Dvd); var second = Game(GameMedium.Dvd) with { Location = "/second.zip" };
            var inventory = new DeviceInventorySnapshot(1, [Device("source"), Optical("dvd1"), Optical("dvd2"), Device("psp0", true)]);
            var (f, repository) = await Batch([first, second], inventory: inventory); await using var context = f;
            Hold(f); var preferences = f.Session.Snapshot.Associations;
            Select(f, first, second);
            Assert.All(f.Main.CopyDrafts, draft => Assert.Null(draft.SelectedSource));
            f.Main.CopyDrafts[0].SelectedSource = f.Main.CopyDrafts[0].Sources[0];
            f.Main.CopyDrafts[1].SelectedSource = f.Main.CopyDrafts[1].Sources[separate ? 1 : 0];
            f.Main.SendPreparedCopiesCommand.Execute(null);
            var result = f.Main.LastCopyAdmission!; Assert.Equal(2, result.Accepted.Length); Assert.Empty(result.Incidents);
            Assert.Equal(!separate, result.Accepted[0].Source.Device == result.Accepted[1].Source.Device);
            Assert.Same(preferences, f.Session.Snapshot.Associations); Assert.Equal(0, repository.Writes);
            await f.Startup.Coordinator!.CancelAllAsync();
        });
    }

    [Fact]
    public async Task CatalogRefreshReconcilesDraftsByIdAndCrudAffectsOnlyTheFocusedGame()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var first = Game(); var second = Game() with { Location = "/second.zip" };
            var (f, repository) = await Batch([first, second]); await using var context = f;
            Hold(f); Select(f, first, second); f.Main.CopyDrafts[1].SelectedPsp = null;
            f.Main.SendPreparedCopiesCommand.Execute(null); var captured = f.Main.LastCopyAdmission!.Accepted.Single();
            await Wait(() => f.Copy.Operations.Count == 1 && f.Copy.Operations[0].Status == "Transferring");
            f.Main.SelectedGame = second; f.Main.StartEditCommand.Execute(null); f.Main.Editor.Title = "Edited draft";
            f.Main.SaveChangesCommand.Execute(null);
            Assert.Equal(1, repository.Writes); Assert.Equal(first, repository.GetById(first.Id));
            Assert.Equal("Edited draft", f.Main.SelectedGames.Single(game => game.Id == second.Id).Title);
            Assert.Equal("Edited draft", Assert.Single(f.Main.CopyDrafts).Title);
            Assert.Equal(captured, f.Copy.Operations[0].Request);
            f.Main.SelectedGame = first; f.Main.DeleteSelectedCommand.Execute(null); f.Main.ConfirmDeleteCommand.Execute(null);
            Assert.Equal(2, repository.Writes); Assert.Null(repository.GetById(first.Id));
            Assert.DoesNotContain(f.Main.SelectedGames, game => game.Id == first.Id);
            Assert.True(f.Copy.Operations[0].IsBusy); Assert.Equal(captured, f.Copy.Operations[0].Request);
            await f.Startup.Coordinator!.CancelAllAsync();
        });
    }

    [Fact]
    public async Task DeviceUpdatesShowAnIncidentWithoutFallbackOrAutomaticSend()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var game = Game(); var (f, _) = await Batch([game]); await using var context = f;
            Select(f, game); Assert.True(f.Main.CopyDrafts[0].IsReady);
            var old = f.Main.CopyDrafts[0].SelectedSource!.Device.Key;
            f.Devices.Publish(new(2, f.Devices.Inventory.Devices.Where(device => device.Key != old).ToImmutableArray()));
            await Wait(() => !f.Main.CopyDrafts[0].IsReady);
            f.Main.SendPreparedCopiesCommand.Execute(null);
            Assert.Empty(f.Main.LastCopyAdmission!.Accepted); Assert.Single(f.Main.LastCopyAdmission.Incidents);
            Assert.Equal(old, f.Main.CopyDrafts[0].SelectedSource!.Device.Key); Assert.Equal(0, f.IO.Opens);
            f.Main.CopyDrafts[0].SelectedSource = f.Main.CopyDrafts[0].Sources.Single(option => option.Device.Key != old);
            Assert.True(f.Main.CopyDrafts[0].IsReady); Assert.Equal(0, f.IO.Opens);
        });
    }

    [Fact]
    public async Task TypedSelectionAndDraftControlsWorkAtMinimumSizeAndLegacyCompositionStaysSingle()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var first = Game(); var second = Game() with { Location = "/second.zip", Title = "Long title " + new string('x', 300) };
            var (f, repository) = await Batch([first, second]); await using var context = f;
            var window = new MainWindow { DataContext = f.Main, Width = 900, Height = 450 };
            window.Show(); window.UpdateLayout();
            var list = window.FindControl<ListBox>("GamesList")!;
            Assert.Equal(SelectionMode.Multiple, list.SelectionMode);
            list.SelectedItems!.Clear(); Assert.True(list.ContainerFromIndex(0)!.Focus());
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(2, f.Main.SelectedGames.Count); Assert.NotNull(f.Main.SelectedGame);
            window.FindControl<Button>("PrepareSelectedCopiesButton")!.Command!.Execute(null); window.UpdateLayout();
            Assert.Equal(2, f.Main.CopyDrafts.Count); Assert.Equal(0, f.IO.Opens);
            var panel = window.FindControl<CopySubmissionView>("CopySubmissionPanel")!;
            var psps = panel.GetVisualDescendants().OfType<ComboBox>().Where(control => control.Name == "CopyDestinationPspInput").ToArray();
            Assert.Equal(2, psps.Length); Assert.Equal("Destination PSP", AutomationProperties.GetName(psps[0]));
            psps[0].SelectedItem = null; Assert.Null(f.Main.CopyDrafts[0].SelectedPsp); Assert.NotNull(f.Main.CopyDrafts[1].SelectedPsp);
            var source = f.Main.CopyDrafts[1].SelectedSource;
            f.Devices.Publish(new(2, f.Devices.Inventory.Devices.Select(device => device with { DisplayName = device.DisplayName + " updated" }).ToImmutableArray()));
            await Wait(() => f.DeviceModel.SubmissionObservation!.Inventory.Revision > 1);
            Assert.Equal(source!.Device.Key, f.Main.CopyDrafts[1].SelectedSource!.Device.Key);
            Assert.NotNull(f.Main.CopyDrafts[1].SelectedVolume); Assert.NotNull(f.Main.CopyDrafts[1].SelectedPsp);
            var send = panel.FindControl<Button>("SendPreparedCopiesButton")!;
            Assert.True(send.Focus()); Assert.True(send.Command!.CanExecute(null));
            var scroll = panel.FindControl<ScrollViewer>("CopyDraftScrollViewer")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.InRange(panel.TranslatePoint(default, window)!.Value.Y + panel.Bounds.Height, 1, 450);
            Hold(f); send.Command.Execute(null);
            Assert.Single(f.Main.LastCopyAdmission!.Accepted); Assert.Single(f.Main.LastCopyAdmission.Incidents);
            await f.Startup.Coordinator!.CancelAllAsync();
            Assert.Equal(0, repository.Writes); window.Close();
            var legacy = new MainViewModel(new GameCatalogService(repository), null); legacy.AttachCopy(f.Copy);
            var legacyWindow = new MainWindow { DataContext = legacy }; legacyWindow.Show();
            Assert.True(legacy.IsCopyPreparationEnabled);
            Assert.Equal(SelectionMode.Multiple, legacyWindow.FindControl<ListBox>("GamesList")!.SelectionMode);
            Assert.False(legacy.PrepareSelectedCopiesCommand.CanExecute(null)); legacy.AttachCopy(null); legacyWindow.Close();
        });
    }

    [Fact]
    public async Task MountedCatalogRefreshPreservesSelectedIdsAndDraftChoices()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var first = Game(); var second = Game() with { Location = "/second.zip" };
            var (f, repository) = await Batch([first, second]); await using var context = f;
            var window = new MainWindow { DataContext = f.Main, Width = 900, Height = 450 }; window.Show(); window.UpdateLayout();
            var list = window.FindControl<ListBox>("GamesList")!;
            list.SelectedItems!.Clear(); list.SelectedItems.Add(first); list.SelectedItems.Add(second);
            f.Main.PrepareSelectedCopiesCommand.Execute(null); window.UpdateLayout();
            f.Main.SelectedGame = first; f.Main.StartEditCommand.Execute(null); f.Main.Editor.Title = "Changed title";
            f.Main.SaveChangesCommand.Execute(null); window.UpdateLayout();
            Assert.Equal(2, f.Main.SelectedGames.Count);
            Assert.Equal("Changed title", f.Main.SelectedGames.Single(game => game.Id == first.Id).Title);
            Assert.Equal("Changed title", f.Main.CopyDrafts.Single(draft => draft.Game.Id == first.Id).Title);
            Assert.All(f.Main.CopyDrafts, draft => Assert.True(draft.IsReady));
            Assert.Equal(1, repository.Writes); Assert.Equal(0, f.IO.Opens); window.Close();
        });
    }

    [Fact]
    public async Task CoordinatorIncidentsKeepOriginalBatchIndicesAndDoNotRollbackReadyGames()
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var games = Enumerable.Range(0, 4).Select(index => Game() with { Location = $"/game{index}.zip" }).ToArray();
            var (f, repository) = await Batch(games); await using var context = f;
            Hold(f); Select(f, games[0]); f.Main.SendPreparedCopiesCommand.Execute(null);
            var active = f.Main.LastCopyAdmission!.Accepted.Single();
            await Wait(() => f.Copy.Operations.Count == 1 && f.Copy.Operations[0].Status == "Transferring");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.IO.ClosePause = () => { entered.TrySetResult(); return release.Task; };
            var removal = f.Startup.Coordinator!.RemoveDeviceAsync(active.Source.Device, f.Session.Snapshot.Inventory).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Select(f, games[1], games[2], games[3]); f.Main.CopyDrafts[0].SelectedPsp = null;
                var independent = f.Main.CopyDrafts[2];
                independent.SelectedSource = independent.Sources.Single(option => option.Device.Key != active.Source.Device);
                f.Main.SendPreparedCopiesCommand.Execute(null);
                var result = f.Main.LastCopyAdmission!;
                Assert.Equal(games[3].Id, Assert.Single(result.Accepted).Game.Id);
                Assert.Equal(new[] { 0, 1 }, result.Incidents.Select(incident => incident.SubmissionIndex));
                Assert.Equal(GameCopyErrorCode.CopyInProgress, result.Incidents[1].Error.Code);
                Assert.Equal(2, f.Main.CopyDrafts.Count); Assert.Equal(0, repository.Writes);
                Assert.False(removal.IsCompleted);
            }
            finally { release.TrySetResult(); await removal; }
            await f.Startup.Coordinator.CancelAllAsync();
        });
    }

    internal sealed class MutableRepository(params GameRecord[] games) : IGameRecordRepository
    {
        internal int Writes;
        private readonly List<GameRecord> records = [.. games];
        public IReadOnlyList<GameRecord> List() => records.ToArray();
        public GameRecord? GetById(Guid id) => records.FirstOrDefault(game => game.Id == id);
        public void Create(GameRecord record) { Writes++; records.Add(record); }
        public void Update(GameRecord record) { Writes++; records[records.FindIndex(game => game.Id == record.Id)] = record; }
        public void Delete(Guid id) { Writes++; records.RemoveAll(game => game.Id == id); }
    }
}
