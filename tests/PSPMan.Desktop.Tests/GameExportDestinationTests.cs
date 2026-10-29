using PSPMan.Desktop.Services;

namespace PSPMan.Desktop.Tests;

public class GameExportDestinationTests
{
    [Fact]
    public void ResolvesCsvNameInsideSelectedFolderWithoutCheckingItsExistence()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var catalogPath = Path.Combine(folder, "catalog.db");

        var valid = GameExportDestination.TryResolve(folder, "PSP Games.CSV", catalogPath,
            out var destinationPath, out var error);

        Assert.True(valid);
        Assert.Equal(Path.Combine(folder, "PSP Games.CSV"), destinationPath);
        Assert.Null(error);
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".csv")]
    [InlineData("report.txt")]
    [InlineData("game.csv ")]
    [InlineData("../game.csv")]
    [InlineData("..\\game.csv")]
    [InlineData("sub/game.csv")]
    [InlineData("sub\\game.csv")]
    [InlineData("/absolute.csv")]
    [InlineData("bad?.csv")]
    [InlineData("bad\0.csv")]
    [InlineData("CON.csv")]
    [InlineData("NUL.extra.csv")]
    public void RejectsNamesThatAreNotPortableSingleCsvFilenames(string? fileName)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests");

        var valid = GameExportDestination.TryResolve(folder, fileName,
            Path.Combine(folder, "catalog.db"), out var destinationPath, out var error);

        Assert.False(valid);
        Assert.Null(destinationPath);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/folder")]
    public void RejectsMissingOrRelativeSelectedFolder(string? folder)
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), "catalog.db");

        var valid = GameExportDestination.TryResolve(folder, "games.csv", catalogPath,
            out var destinationPath, out var error);

        Assert.False(valid);
        Assert.Null(destinationPath);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RejectsTheCatalogItselfAsTheExportDestination()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PSPMan.Export.Tests");
        var catalogPath = Path.Combine(folder, "catalog.csv");

        var valid = GameExportDestination.TryResolve(folder, "catalog.csv", catalogPath,
            out var destinationPath, out var error);

        Assert.False(valid);
        Assert.Null(destinationPath);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
