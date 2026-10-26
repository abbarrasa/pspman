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

    [Theory]
    [InlineData("/game.rar")]
    [InlineData("/DevHook/GAMES/Key of Heaven.rar")]
    [InlineData("/mnt/usb/game.rar")]
    [InlineData("/Games/Mi Juego.iso")]
    public void MediumRootedFilePathIsAcceptedWithoutAccessingTheMedium(string location)
    {
        Assert.Empty(GameRecordRules.Validate(ValidInput with { Location = location }));
    }

    [Theory]
    [InlineData("game.rar")]
    [InlineData("C:\\Games\\game.rar")]
    [InlineData("/")]
    [InlineData("/Games/")]
    [InlineData("/Games//game.rar")]
    [InlineData("//game.rar")]
    [InlineData("/./game.rar")]
    [InlineData("/Games/../game.rar")]
    [InlineData("/Games/./game.rar")]
    [InlineData("/Games\\game.rar")]
    [InlineData("/Games/game\u0000.rar")]
    [InlineData("/Games/game\u001F.rar")]
    [InlineData("/Games/game\u007F.rar")]
    public void MalformedLocationHasAFieldSpecificError(string location)
    {
        var errors = GameRecordRules.Validate(ValidInput with { Location = location });

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

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void NormalizeDvdNumberConvertsOnlyZeroToNull(int? entered, int? expected)
    {
        Assert.Equal(expected, GameRecordRules.NormalizeDvdNumber(entered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void DvdRequiresAPositiveNumber(int? dvdNumber)
    {
        var errors = GameRecordRules.Validate(ValidInput with
        {
            Medium = GameMedium.Dvd,
            DvdNumber = dvdNumber
        });

        Assert.Equal(nameof(GameRecordInput.DvdNumber), Assert.Single(errors).Field);
    }

    [Fact]
    public void DvdAcceptsAPositiveNumber()
    {
        Assert.Empty(GameRecordRules.Validate(ValidInput with
        {
            Medium = GameMedium.Dvd,
            DvdNumber = int.MaxValue
        }));
    }

    [Theory]
    [InlineData(GameMedium.Usb, 1)]
    [InlineData(GameMedium.MemoryCard, -1)]
    public void NonDvdMediumRejectsANumber(GameMedium medium, int dvdNumber)
    {
        var errors = GameRecordRules.Validate(ValidInput with
        {
            Medium = medium,
            DvdNumber = dvdNumber
        });

        Assert.Equal(nameof(GameRecordInput.DvdNumber), Assert.Single(errors).Field);
    }

    [Theory]
    [InlineData(GameMedium.Usb)]
    [InlineData(GameMedium.MemoryCard)]
    public void NonDvdMediumTreatsZeroAsNoNumber(GameMedium medium)
    {
        Assert.Empty(GameRecordRules.Validate(ValidInput with
        {
            Medium = medium,
            DvdNumber = 0
        }));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void SizeMustBePositive(long sizeBytes)
    {
        var errors = GameRecordRules.Validate(ValidInput with { SizeBytes = sizeBytes });

        Assert.Equal(nameof(GameRecordInput.SizeBytes), Assert.Single(errors).Field);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void PositiveSizeIsAccepted(long sizeBytes)
    {
        Assert.Empty(GameRecordRules.Validate(ValidInput with { SizeBytes = sizeBytes }));
    }

    [Fact]
    public void NotesAtLimitAreAcceptedAndOneCharacterOverIsRejected()
    {
        Assert.Empty(GameRecordRules.Validate(ValidInput with { Notes = new string('x', 2_500) }));

        var errors = GameRecordRules.Validate(ValidInput with { Notes = new string('x', 2_501) });

        Assert.Equal(nameof(GameRecordInput.Notes), Assert.Single(errors).Field);
    }

    [Fact]
    public void EmptyNotesAreAccepted()
    {
        Assert.Empty(GameRecordRules.Validate(ValidInput with { Notes = string.Empty }));
    }

    [Fact]
    public void NotesLimitCountsUtf16CodeUnits()
    {
        var emoji = "🎮";

        Assert.Empty(GameRecordRules.Validate(ValidInput with { Notes = string.Concat(Enumerable.Repeat(emoji, 1_250)) }));
        Assert.Equal(nameof(GameRecordInput.Notes), Assert.Single(GameRecordRules.Validate(
            ValidInput with { Notes = string.Concat(Enumerable.Repeat(emoji, 1_251)) })).Field);
    }

    [Fact]
    public void IdentityIgnoresTitleFormatSizeAndNotes()
    {
        var saved = SavedRecord(ValidInput);
        var candidate = ValidInput with
        {
            Title = "A different title",
            Format = GameFormat.Chd,
            SizeBytes = 42,
            Notes = "Different notes"
        };

        Assert.True(GameRecordRules.HasSameIdentity(candidate, saved));
    }

    [Fact]
    public void IdentityComparesLocationWithExactCase()
    {
        var saved = SavedRecord(ValidInput);

        Assert.True(GameRecordRules.HasSameIdentity(ValidInput, saved));
        Assert.False(GameRecordRules.HasSameIdentity(
            ValidInput with { Location = "/games/Key of Heaven.rar" }, saved));
        Assert.False(GameRecordRules.HasSameIdentity(
            ValidInput with { Location = "/GAMES/Key of Heaven.rar " }, saved));
    }

    [Fact]
    public void IdentityDistinguishesMediaWithTheSameLocation()
    {
        var saved = SavedRecord(ValidInput);

        Assert.False(GameRecordRules.HasSameIdentity(
            ValidInput with { Medium = GameMedium.MemoryCard }, saved));
        Assert.False(GameRecordRules.HasSameIdentity(
            ValidInput with { Medium = GameMedium.Dvd, DvdNumber = 1 }, saved));
    }

    [Fact]
    public void DvdIdentityIncludesDvdNumber()
    {
        var dvd = ValidInput with { Medium = GameMedium.Dvd, DvdNumber = 2 };
        var saved = SavedRecord(dvd);

        Assert.True(GameRecordRules.HasSameIdentity(dvd, saved));
        Assert.False(GameRecordRules.HasSameIdentity(dvd with { DvdNumber = 1 }, saved));
    }

    [Fact]
    public void NonDvdIdentityIgnoresNormalizedZeroDvdNumber()
    {
        var saved = SavedRecord(ValidInput);

        Assert.True(GameRecordRules.HasSameIdentity(
            ValidInput with { DvdNumber = 0 }, saved));
    }

    private static GameRecord SavedRecord(GameRecordInput input) => new(
        Guid.NewGuid(),
        input.Title,
        input.Medium,
        input.DvdNumber,
        input.Location,
        input.Format,
        input.SizeBytes,
        input.Notes);
}
