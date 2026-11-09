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

    [Theory]
    [InlineData(@"E:\", "/GAMES/Game.rar", @"E:\GAMES\Game.rar")]
    [InlineData(@"e:\", "/games/GAME.rar", @"e:\games\GAME.rar")]
    [InlineData(@"C:\Mounted\Games", "/GAMES/Game.rar", @"C:\Mounted\Games\GAMES\Game.rar")]
    [InlineData(@"C:\Mounted\Games\", "/GAMES/Game.rar", @"C:\Mounted\Games\GAMES\Game.rar")]
    [InlineData(@"E:\My Games\", "/日本語/Árbol Game.rar", @"E:\My Games\日本語\Árbol Game.rar")]
    [InlineData(@"E:\", "/Collection/GAMES/Game.rar", @"E:\Collection\GAMES\Game.rar")]
    [InlineData(@"E:\", "/.hidden/COM10.rar", @"E:\.hidden\COM10.rar")]
    [InlineData(@"E:\", "/ Leading space/Game.rar", @"E:\ Leading space\Game.rar")]
    public void ConstructsWindowsPathsIndependentlyOfTheHost(string root, string location, string expected)
    {
        var result = MountedGamePathRules.Resolve(root, location, MountedPathSyntax.Windows);

        Assert.Equal(expected, result.Path);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"E:")]
    [InlineData(@"E:Games")]
    [InlineData(@"\Games")]
    [InlineData(@"Games")]
    [InlineData("/media/games")]
    [InlineData("E:/Games")]
    [InlineData(@"1:\")]
    [InlineData(@"E:\Games\..\Other")]
    [InlineData(@"E:\Games\.\Other")]
    [InlineData(@"E:\Games\\Other")]
    [InlineData(@"E:\Bad?Name")]
    [InlineData(@"E:\CON")]
    [InlineData(@"E:\Games.")]
    [InlineData(@"E:\Games ")]
    [InlineData("E:\\Games\0")]
    [InlineData(@"\\.\PhysicalDrive0")]
    public void RejectsInvalidWindowsRootsWithoutReturningAPath(string? root)
    {
        var result = MountedGamePathRules.Resolve(root, "/GAMES/Game.rar", MountedPathSyntax.Windows);

        Assert.Null(result.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("/GAMES/Bad<Name.rar")]
    [InlineData("/GAMES/Bad>Name.rar")]
    [InlineData("/GAMES/Game.rar:stream")]
    [InlineData("/GAMES/Bad\"Name.rar")]
    [InlineData("/GAMES/Bad|Name.rar")]
    [InlineData("/GAMES/Bad?Name.rar")]
    [InlineData("/GAMES/Bad*Name.rar")]
    [InlineData("/GAMES/CON")]
    [InlineData("/GAMES/con.rar")]
    [InlineData("/GAMES/PRN.iso")]
    [InlineData("/GAMES/AUX")]
    [InlineData("/GAMES/NUL.tar.gz")]
    [InlineData("/GAMES/COM1.chd")]
    [InlineData("/GAMES/com9")]
    [InlineData("/GAMES/LPT1")]
    [InlineData("/GAMES/lpt9.rar")]
    [InlineData("/GAMES/COM¹.rar")]
    [InlineData("/GAMES/LPT²")]
    [InlineData("/GAMES/COM³")]
    [InlineData("/GAMES/Game.rar.")]
    [InlineData("/GAMES/Game.rar ")]
    [InlineData("/Games /Game.rar")]
    [InlineData("/NUL/Game.rar")]
    [InlineData("/../Game.rar")]
    [InlineData("/GAMES//Game.rar")]
    [InlineData("/GAMES/./Game.rar")]
    [InlineData(@"/GAMES\Game.rar")]
    public void RejectsWindowsInvalidNamesWithoutSanitizingTheLocation(string location)
    {
        var original = location;
        var result = MountedGamePathRules.Resolve(@"E:\", location, MountedPathSyntax.Windows);

        Assert.Null(result.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(original, location);
    }

    [Fact]
    public void WindowsCompositionDoesNotProbeFilesOrChangeTheStoredLocation()
    {
        var location = "/Absent/" + Guid.NewGuid().ToString("N") + ".rar";
        var record = new GameRecord(Guid.NewGuid(), "Game", GameMedium.Usb, null,
            location, GameFormat.Iso, 1, null);

        var result = MountedGamePathRules.Resolve(@"Z:\NotAnObservedVolume", record.Location, MountedPathSyntax.Windows);

        Assert.Equal(@"Z:\NotAnObservedVolume" + location.Replace('/', '\\'), result.Path);
        Assert.Null(result.Error);
        Assert.Equal(location, record.Location);
    }

}
