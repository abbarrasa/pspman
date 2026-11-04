using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.Tests;

public class GameImportFilePickerTests
{
    [Fact]
    public async Task ReturnsSelectedLocalPathWithoutOpeningOrCreatingTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "PSPMan.Import.Picker.Tests", Guid.NewGuid() + ".csv");
        IGameImportFilePicker picker = new AvaloniaGameImportFilePicker(
            () => Task.FromResult<IReadOnlyList<string?>>([path]));

        Assert.Equal(path, await picker.PickFileAsync());
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task ReturnsNullWhenSelectionIsCancelled()
    {
        IGameImportFilePicker picker = new AvaloniaGameImportFilePicker(
            () => Task.FromResult<IReadOnlyList<string?>>([]));

        Assert.Null(await picker.PickFileAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("relative/catalog.csv")]
    [InlineData("https://example.com/catalog.csv")]
    public async Task RejectsSelectionWithoutAUsableLocalPath(string? path)
    {
        IGameImportFilePicker picker = new AvaloniaGameImportFilePicker(
            () => Task.FromResult<IReadOnlyList<string?>>([path]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(picker.PickFileAsync);

        Assert.Equal("The selected file has no usable local path.", error.Message);
    }

    [Fact]
    public async Task PickerFailureIsNotTreatedAsCancellation()
    {
        var failure = new InvalidOperationException("File selection is unavailable on this platform.");
        IGameImportFilePicker picker = new AvaloniaGameImportFilePicker(
            () => Task.FromException<IReadOnlyList<string?>>(failure));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(picker.PickFileAsync));
    }
}
