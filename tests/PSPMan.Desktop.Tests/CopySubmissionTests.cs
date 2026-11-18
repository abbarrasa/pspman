using System.Collections;
using PSPMan.Desktop.ViewModels;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class CopySubmissionTests
{
    [Fact]
    public async Task CopySelectionIsSeparateFromFocusedCatalogGameAndDoesNotStartIo()
    {
        await using var f = await Create(post: action => action());
        var property = typeof(MainViewModel).GetProperty("SelectedGames");
        Assert.NotNull(property);
        var selection = Assert.IsAssignableFrom<IList>(property.GetValue(f.Main));
        Assert.Empty(selection);
        selection.Add(f.Game);
        Assert.Same(f.Game, f.Main.SelectedGame);
        Assert.Equal(0, f.IO.Opens); Assert.Equal(0, f.Catalog.Writes);
    }
}
