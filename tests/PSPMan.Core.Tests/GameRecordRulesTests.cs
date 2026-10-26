using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameRecordRulesTests
{
    private static readonly GameRecordInput ValidInput = new(
        "Key of Heaven",
        GameMedium.Usb,
        null,
        "/GAMES/Key of Heaven.rar",
        GameFormat.Iso,
        1_073_741_824,
        null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\u00A0\u2003\u3000")]
    public void TitleMustContainANonWhitespaceCharacter(string? title)
    {
        var errors = GameRecordRules.Validate(ValidInput with { Title = title! });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(GameRecordInput.Title), error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\t\u00A0\n")]
    public void LocationIsRequired(string? location)
    {
        var errors = GameRecordRules.Validate(ValidInput with { Location = location! });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(GameRecordInput.Location), error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void UnsupportedMediumAndFormatHaveSeparateFieldErrors()
    {
        var errors = GameRecordRules.Validate(ValidInput with
        {
            Medium = (GameMedium)999,
            Format = (GameFormat)999
        });

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Field == nameof(GameRecordInput.Medium));
        Assert.Contains(errors, error => error.Field == nameof(GameRecordInput.Format));
        Assert.All(errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }

    [Theory]
    [InlineData(GameMedium.Dvd, GameFormat.Iso)]
    [InlineData(GameMedium.Usb, GameFormat.Chd)]
    [InlineData(GameMedium.MemoryCard, GameFormat.Iso)]
    public void AllowedChoicesAndPopulatedFieldsHaveNoCurrentRuleErrors(
        GameMedium medium,
        GameFormat format)
    {
        var errors = GameRecordRules.Validate(ValidInput with
        {
            Medium = medium,
            Format = format,
            DvdNumber = medium == GameMedium.Dvd ? 1 : null
        });

        Assert.Empty(errors);
    }
}
