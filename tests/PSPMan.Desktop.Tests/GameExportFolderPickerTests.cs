using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.Tests;

public class GameExportFolderPickerTests
{
    [Fact]
    public async Task ReturnsSelectedLocalFolderWithoutCreatingFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Picker.Tests", Guid.NewGuid().ToString("N"));
        IGameExportFolderPicker picker = new AvaloniaGameExportFolderPicker(
            () => Task.FromResult<IReadOnlyList<string?>>([folder]));

        var selected = await picker.PickFolderAsync();

        Assert.Equal(folder, selected);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ReturnsNullWhenSelectionIsCancelled()
    {
        IGameExportFolderPicker picker = new AvaloniaGameExportFolderPicker(
            () => Task.FromResult<IReadOnlyList<string?>>(Array.Empty<string?>()));

        Assert.Null(await picker.PickFolderAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/export")]
    [InlineData("relative/export")]
    public async Task RejectsSelectionWithoutAUsableLocalPath(string? path)
    {
        IGameExportFolderPicker picker = new AvaloniaGameExportFolderPicker(
            () => Task.FromResult<IReadOnlyList<string?>>([path]));

        await Assert.ThrowsAsync<InvalidOperationException>(picker.PickFolderAsync);
    }
}
