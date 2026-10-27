using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public class GameEditorViewModelTests
{
    [Fact]
    public void EditorOffersOnlySupportedMediumsAndFormats()
    {
        var editor = new GameEditorViewModel();

        Assert.Equal(
            [(GameMedium.Dvd, "DVD"), (GameMedium.Usb, "USB"), (GameMedium.MemoryCard, "Memory Card")],
            editor.MediumChoices.Select(choice => (choice.Value, choice.Label)));
        Assert.Equal(
            [(GameFormat.Iso, "ISO"), (GameFormat.Chd, "CHD")],
            editor.FormatChoices.Select(choice => (choice.Value, choice.Label)));
        Assert.Null(editor.SelectedMedium);
        Assert.Null(editor.SelectedFormat);
    }

    [Fact]
    public void DvdNumberAppearsOnlyForDvdAndIsClearedWhenMediumChanges()
    {
        var editor = new GameEditorViewModel();

        Assert.False(editor.ShowDvdNumber);
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Dvd);
        editor.ValidateFields();
        Assert.True(editor.HasDvdNumberError);
        editor.DvdNumberText = "2";
        Assert.True(editor.ShowDvdNumber);
        Assert.False(editor.HasDvdNumberError);

        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Usb);

        Assert.False(editor.ShowDvdNumber);
        Assert.Equal(string.Empty, editor.DvdNumberText);
        Assert.False(editor.HasDvdNumberError);
    }

    [Fact]
    public void ValidFieldsProduceAnInputWithoutPersistingIt()
    {
        var editor = new GameEditorViewModel
        {
            Title = "Key of Heaven",
            Location = "/DevHook/GAMES/Key of Heaven.rar",
            DvdNumberText = "2",
            SizeBytesText = "1572864",
            Notes = "Disc two"
        };
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Dvd);
        editor.DvdNumberText = "2";
        editor.SelectedFormat = editor.FormatChoices.Single(choice => choice.Value == GameFormat.Iso);

        var input = editor.ValidateFields();

        Assert.Equal(new GameRecordInput("Key of Heaven", GameMedium.Dvd, 2,
            "/DevHook/GAMES/Key of Heaven.rar", GameFormat.Iso, 1_572_864, "Disc two"), input);
        Assert.False(editor.HasAnyFieldError);
        Assert.Equal("1.5 MiB", editor.HumanReadableSizeText);
    }

    [Fact]
    public void CheckFieldsShowsCoreFieldErrorsAndDoesNotRequireStorage()
    {
        var editor = new GameEditorViewModel
        {
            Title = " \t ",
            Location = "relative/game.rar",
            SizeBytesText = "0",
            Notes = new string('x', 2_501)
        };
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Dvd);
        editor.SelectedFormat = editor.FormatChoices.Single(choice => choice.Value == GameFormat.Chd);
        editor.DvdNumberText = "0";

        editor.CheckFieldsCommand.Execute(null);

        Assert.True(editor.HasAnyFieldError);
        Assert.Equal("Title is required.", editor.TitleError);
        Assert.Equal("Location must be a complete path from the medium root.", editor.LocationError);
        Assert.Equal("DVD number must be a positive integer.", editor.DvdNumberError);
        Assert.Equal("Size must be a positive byte count.", editor.SizeBytesError);
        Assert.Equal("Notes cannot exceed 2,500 UTF-16 code units.", editor.NotesError);

        editor.Title = "Valid title";
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Usb);

        Assert.False(editor.HasTitleError);
        Assert.False(editor.HasMediumError);
        Assert.False(editor.HasDvdNumberError);
        Assert.True(editor.HasLocationError);
    }

    [Theory]
    [InlineData("abc", "Size must be a whole number of bytes.")]
    [InlineData("9223372036854775808", "Size must be a whole number of bytes.")]
    [InlineData("-1", "Size must be a positive byte count.")]
    public void SizeErrorsExplainMalformedAndNonPositiveValues(string size, string expectedError)
    {
        var editor = ValidUsbEditor();
        editor.SizeBytesText = size;

        Assert.Null(editor.ValidateFields());

        Assert.Equal(expectedError, editor.SizeBytesError);
        Assert.Equal(string.Empty, editor.HumanReadableSizeText);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public void DvdNumberErrorsExplainMalformedValues(string dvdNumber)
    {
        var editor = ValidUsbEditor();
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Dvd);
        editor.DvdNumberText = dvdNumber;

        Assert.Null(editor.ValidateFields());

        Assert.Equal("DVD number must be a whole number.", editor.DvdNumberError);
    }

    [Theory]
    [InlineData("1", "1 B")]
    [InlineData("1024", "1 KiB")]
    [InlineData("1073741824", "1 GiB")]
    public void HumanReadablePreviewUsesBinaryUnits(string bytes, string expected)
    {
        var editor = new GameEditorViewModel { SizeBytesText = bytes };

        Assert.Equal(expected, editor.HumanReadableSizeText);
    }

    private static GameEditorViewModel ValidUsbEditor()
    {
        var editor = new GameEditorViewModel
        {
            Title = "Game",
            Location = "/GAMES/Game.rar",
            SizeBytesText = "10"
        };
        editor.SelectedMedium = editor.MediumChoices.Single(choice => choice.Value == GameMedium.Usb);
        editor.SelectedFormat = editor.FormatChoices.Single(choice => choice.Value == GameFormat.Chd);
        return editor;
    }
}
