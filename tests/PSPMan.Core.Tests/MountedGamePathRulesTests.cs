using PSPMan.Core.Devices;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public sealed class MountedGamePathRulesTests
{
    [Theory]
    [InlineData("/media/collection", "/GAMES/Game.rar", "/media/collection/GAMES/Game.rar")]
    [InlineData("/media/collection/", "/GAMES/Game.rar", "/media/collection/GAMES/Game.rar")]
    [InlineData("/", "/GAMES/Game.rar", "/GAMES/Game.rar")]
    [InlineData("/media/Collection", "/Collection/GAMES/Game.rar", "/media/Collection/Collection/GAMES/Game.rar")]
    [InlineData("/media/My Games", "/日本語/Árbol Game.rar", "/media/My Games/日本語/Árbol Game.rar")]
    [InlineData("/media/games", "/games/GAME.rar", "/media/games/games/GAME.rar")]
    [InlineData("/media/games", "/ Games / File.rar ", "/media/games/ Games / File.rar ")]
    [InlineData("/media/games", "/GAMES/Name:Part?1*.rar", "/media/games/GAMES/Name:Part?1*.rar")]
    public void RetainsTheRootAndEveryLocationComponent(string root, string location, string expected)
    {
        var result = MountedGamePathRules.Resolve(root, location, MountedPathSyntax.Linux);

        Assert.Equal(expected, result.Path);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("GAMES/Game.rar")]
    [InlineData("/")]
    [InlineData("//GAMES/Game.rar")]
    [InlineData("/GAMES//Game.rar")]
    [InlineData("/GAMES/")]
    [InlineData("/../Game.rar")]
    [InlineData("/GAMES/../../Game.rar")]
    [InlineData("/./Game.rar")]
    [InlineData("/GAMES\\Game.rar")]
    [InlineData("/GAMES/Game\0.rar")]
    [InlineData("/GAMES/Game\n.rar")]
    public void RejectsInvalidLocationsWithoutReturningAPath(string? location)
    {
        var result = MountedGamePathRules.Resolve("/media/games", location, MountedPathSyntax.Linux);

        Assert.Null(result.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("media/games")]
    [InlineData("E:\\")]
    [InlineData("/media/../games")]
    [InlineData("/media/./games")]
    [InlineData("/media//games")]
    [InlineData("/media/games\0")]
    public void RejectsInvalidMountRootsWithoutReturningAPath(string? root)
    {
        var result = MountedGamePathRules.Resolve(root, "/GAMES/Game.rar", MountedPathSyntax.Linux);

        Assert.Null(result.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ResolvesNonexistentPathsWithoutInspectingOrChangingTheCatalogRecord()
    {
        var record = new GameRecord(Guid.NewGuid(), "Game", GameMedium.Usb, null,
            "/NotAVolumeLabel/Game.rar", GameFormat.Iso, 1, null);
        var original = record with { };
        var root = "/unobserved-test-root/" + Guid.NewGuid().ToString("N");

        var result = MountedGamePathRules.Resolve(root, record.Location, MountedPathSyntax.Linux);

        Assert.Equal(root + record.Location, result.Path);
        Assert.Null(result.Error);
        Assert.Equal(original, record);
    }
}
