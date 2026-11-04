using PSPMan.Core.Games;
using PSPMan.Desktop.Services;
using PSPMan.Desktop.ViewModels;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Desktop.Tests;

public class MainViewModelTemplateTests
{
    [Fact]
    public async Task SavesExactTemplateWithoutReadingOrChangingCatalog()
    {
        using var data = new TemplateData();
        var vm = data.CreateViewModel();
        data.Repository.FailReads = true;

        vm.OpenCsvTemplateCommand.Execute(null);
        Assert.True(vm.IsTemplateExport);
        Assert.Equal("pspman-import-template.csv", vm.ExportFileName);
        await vm.ChooseExportFolderCommand.ExecuteAsync(null);
        vm.ExportCatalogCommand.Execute(null);

        Assert.Equal(GameCatalogCsvTemplate.Generate(), File.ReadAllBytes(data.Destination));
        Assert.Equal(data.Destination, vm.ExportedPath);
        Assert.Null(vm.ExportError);
        Assert.False(vm.IsExportFormOpen);
        data.AssertCatalogUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingFolderSelectionOrFormDoesNotWrite(bool cancelForm)
    {
        using var data = new TemplateData();
        var vm = data.CreateViewModel(cancelPicker: !cancelForm);
        vm.OpenCsvTemplateCommand.Execute(null);
        await vm.ChooseExportFolderCommand.ExecuteAsync(null);
        if (cancelForm) vm.CancelExportCommand.Execute(null);

        Assert.False(vm.IsExportFormOpen);
        Assert.False(File.Exists(data.Destination));
        Assert.Null(vm.ExportedPath);
        data.AssertCatalogUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingFileChangesOnlyAfterReplacementApproval(bool approve)
    {
        using var data = new TemplateData();
        var original = new byte[] { 1, 3, 5, 7 };
        File.WriteAllBytes(data.Destination, original);
        var vm = data.CreateViewModel();
        data.Repository.FailReads = true;
        vm.OpenCsvTemplateCommand.Execute(null);
        await vm.ChooseExportFolderCommand.ExecuteAsync(null);
        vm.ExportCatalogCommand.Execute(null);

        Assert.True(vm.IsReplacementPending);
        Assert.Equal(original, File.ReadAllBytes(data.Destination));
        Assert.Null(vm.ExportedPath);
        if (approve) vm.ConfirmExportReplacementCommand.Execute(null);
        else vm.CancelExportReplacementCommand.Execute(null);

        Assert.Equal(approve ? GameCatalogCsvTemplate.Generate() : original, File.ReadAllBytes(data.Destination));
        Assert.False(vm.IsReplacementPending);
        Assert.Equal(approve ? data.Destination : null, vm.ExportedPath);
        data.AssertCatalogUnchanged();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedWriteReportsTemplateErrorAndPreservesExistingFile(bool replacing)
    {
        using var data = new TemplateData();
        var original = new byte[] { 9, 8, 7 };
        if (replacing) File.WriteAllBytes(data.Destination, original);
        var vm = data.CreateViewModel(writer: new FailingWriter());
        vm.OpenCsvTemplateCommand.Execute(null);
        await vm.ChooseExportFolderCommand.ExecuteAsync(null);
        vm.ExportCatalogCommand.Execute(null);
        if (replacing) vm.ConfirmExportReplacementCommand.Execute(null);

        Assert.True(vm.HasExportError);
        Assert.Contains("template", vm.ExportError!);
        Assert.Null(vm.ExportedPath);
        Assert.False(vm.IsReplacementPending);
        if (replacing) Assert.Equal(original, File.ReadAllBytes(data.Destination));
        else Assert.False(File.Exists(data.Destination));
        data.AssertCatalogUnchanged();
    }

    [Fact]
    public async Task OpeningNormalExportAfterTemplateRestoresCatalogExportMode()
    {
        using var data = new TemplateData();
        var vm = data.CreateViewModel();
        vm.OpenCsvTemplateCommand.Execute(null);
        vm.CancelExportCommand.Execute(null);

        vm.OpenExportCommand.Execute(null);
        Assert.False(vm.IsTemplateExport);
        Assert.Equal("pspman-games.csv", vm.ExportFileName);
        await vm.ChooseExportFolderCommand.ExecuteAsync(null);
        vm.ExportCatalogCommand.Execute(null);

        Assert.Equal(GameCatalogCsvSerializer.SerializeEmptyCatalog(),
            File.ReadAllBytes(Path.Combine(data.Folder, "pspman-games.csv")));
        Assert.Equal(2, data.Repository.ListCalls);
    }

    private sealed class TemplateData : IDisposable
    {
        private readonly byte[] originalCatalog = [0, 2, 4, 6];
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "PSPMan.Template.Tests", Guid.NewGuid().ToString("N"));
        public string CatalogPath => Path.Combine(Folder, "catalog.db");
        public string Destination => Path.Combine(Folder, "pspman-import-template.csv");
        public TrackingRepository Repository { get; } = new();
        public TemplateData() { Directory.CreateDirectory(Folder); File.WriteAllBytes(CatalogPath, originalCatalog); }
        public MainViewModel CreateViewModel(bool cancelPicker = false, IGameCatalogExportWriter? writer = null) =>
            new(new GameCatalogService(Repository), null, new Picker(cancelPicker ? null : Folder),
                writer ?? new AtomicCsvExportWriter(), CatalogPath);
        public void AssertCatalogUnchanged()
        {
            Assert.Equal(1, Repository.ListCalls);
            Assert.Equal(0, Repository.WriteCalls);
            Assert.Equal(originalCatalog, File.ReadAllBytes(CatalogPath));
        }
        public void Dispose() => Directory.Delete(Folder, true);
    }

    private sealed class Picker(string? folder) : IGameExportFolderPicker
    {
        public Task<string?> PickFolderAsync() => Task.FromResult(folder);
    }

    private sealed class FailingWriter : IGameCatalogExportWriter
    {
        public ExportDestinationSnapshot Capture(string path) => new AtomicCsvExportWriter().Capture(path);
        public void Publish(string path, ReadOnlyMemory<byte> content, ExportDestinationSnapshot snapshot, bool overwriteApproved) =>
            throw new IOException("Simulated publication failure.");
    }

    private sealed class TrackingRepository : IGameRecordRepository
    {
        public bool FailReads { get; set; }
        public int ListCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            if (FailReads) throw new IOException("Template must not read the catalog.");
            return [];
        }
        public GameRecord? GetById(Guid id) => throw new InvalidOperationException("Unexpected catalog read.");
        public void Create(GameRecord record) { WriteCalls++; throw new InvalidOperationException("Unexpected catalog write."); }
        public void Update(GameRecord record) { WriteCalls++; throw new InvalidOperationException("Unexpected catalog write."); }
        public void Delete(Guid id) { WriteCalls++; throw new InvalidOperationException("Unexpected catalog write."); }
    }
}
