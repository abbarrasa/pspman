using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameRecordTests
{
    [Fact]
    public void MediumAndFormatExposeOnlySpecifiedChoices()
    {
        Assert.Equal(
            [GameMedium.Dvd, GameMedium.Usb, GameMedium.MemoryCard],
            Enum.GetValues<GameMedium>());
        Assert.Equal(
            [GameFormat.Iso, GameFormat.Chd],
            Enum.GetValues<GameFormat>());
    }

    [Fact]
    public void InputRepresentsEveryEditableFieldAndUsesValueEquality()
    {
        var input = new GameRecordInput(
            "Key of Heaven",
            GameMedium.Dvd,
            2,
            "/DevHook/GAMES/Key of Heaven.rar",
            GameFormat.Iso,
            1_073_741_824,
            "Second disc");

        Assert.Equal(input, input with { });
        Assert.Equal("Key of Heaven", input.Title);
        Assert.Equal(GameMedium.Dvd, input.Medium);
        Assert.Equal(2, input.DvdNumber);
        Assert.Equal("/DevHook/GAMES/Key of Heaven.rar", input.Location);
        Assert.Equal(GameFormat.Iso, input.Format);
        Assert.Equal(1_073_741_824, input.SizeBytes);
        Assert.Equal("Second disc", input.Notes);
    }

    [Fact]
    public void SavedRecordKeepsStableIdAndAllValuesWhenEdited()
    {
        var id = Guid.NewGuid();
        var record = new GameRecord(
            id,
            "Key of Heaven",
            GameMedium.MemoryCard,
            null,
            "/GAMES/Key of Heaven.chd",
            GameFormat.Chd,
            5_000_000_000,
            null);

        var edited = record with { Title = "Key of Heaven (edited)" };

        Assert.Equal(id, record.Id);
        Assert.Equal(id, edited.Id);
        Assert.Equal("Key of Heaven", record.Title);
        Assert.Equal("Key of Heaven (edited)", edited.Title);
        Assert.Equal(GameMedium.MemoryCard, edited.Medium);
        Assert.Null(edited.DvdNumber);
        Assert.Equal("/GAMES/Key of Heaven.chd", edited.Location);
        Assert.Equal(GameFormat.Chd, edited.Format);
        Assert.Equal(5_000_000_000, edited.SizeBytes);
        Assert.Null(edited.Notes);
    }
}
