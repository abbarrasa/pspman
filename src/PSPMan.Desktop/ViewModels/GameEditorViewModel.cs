using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PSPMan.Core.Games;

namespace PSPMan.Desktop.ViewModels;

public sealed record GameMediumChoice(GameMedium Value, string Label);

public sealed record GameFormatChoice(GameFormat Value, string Label);

public partial class GameEditorViewModel : ViewModelBase
{
    private bool _hasCheckedFields;
    private bool _loadedNotesWereNull;

    public IReadOnlyList<GameMediumChoice> MediumChoices { get; } =
    [
        new(GameMedium.Dvd, "DVD"),
        new(GameMedium.Usb, "USB"),
        new(GameMedium.MemoryCard, "Memory Card")
    ];

    public IReadOnlyList<GameFormatChoice> FormatChoices { get; } =
    [
        new(GameFormat.Iso, "ISO"),
        new(GameFormat.Chd, "CHD")
    ];

    public bool ShowDvdNumber => SelectedMedium?.Value == GameMedium.Dvd;

    public string HumanReadableSizeText
    {
        get
        {
            if (!long.TryParse(SizeBytesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                || bytes <= 0)
            {
                return string.Empty;
            }

            return ByteSizeFormatter.Format(bytes);
        }
    }

    public bool HasTitleError => TitleError is not null;

    public bool HasMediumError => MediumError is not null;

    public bool HasDvdNumberError => DvdNumberError is not null;

    public bool HasLocationError => LocationError is not null;

    public bool HasFormatError => FormatError is not null;

    public bool HasSizeBytesError => SizeBytesError is not null;

    public bool HasNotesError => NotesError is not null;

    public bool HasAnyFieldError => HasTitleError || HasMediumError || HasDvdNumberError
        || HasLocationError || HasFormatError || HasSizeBytesError || HasNotesError;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDvdNumber))]
    public partial GameMediumChoice? SelectedMedium { get; set; }

    [ObservableProperty]
    public partial string DvdNumberText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Location { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GameFormatChoice? SelectedFormat { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HumanReadableSizeText))]
    public partial string SizeBytesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTitleError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? TitleError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMediumError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? MediumError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDvdNumberError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? DvdNumberError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocationError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? LocationError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormatError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? FormatError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSizeBytesError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? SizeBytesError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotesError))]
    [NotifyPropertyChangedFor(nameof(HasAnyFieldError))]
    public partial string? NotesError { get; set; }

    partial void OnSelectedMediumChanged(GameMediumChoice? value)
    {
        if (value?.Value != GameMedium.Dvd)
        {
            DvdNumberText = string.Empty;
        }

        RecheckFieldsIfNeeded();
    }

    partial void OnTitleChanged(string value) => RecheckFieldsIfNeeded();

    partial void OnDvdNumberTextChanged(string value) => RecheckFieldsIfNeeded();

    partial void OnLocationChanged(string value) => RecheckFieldsIfNeeded();

    partial void OnSelectedFormatChanged(GameFormatChoice? value) => RecheckFieldsIfNeeded();

    partial void OnSizeBytesTextChanged(string value) => RecheckFieldsIfNeeded();

    partial void OnNotesChanged(string value) => RecheckFieldsIfNeeded();

    [RelayCommand]
    private void CheckFields() => ValidateFields();

    public GameRecordInput? ValidateFields()
    {
        _hasCheckedFields = true;
        ClearErrors();

        var dvdNumber = (int?)null;
        if (ShowDvdNumber && !string.IsNullOrWhiteSpace(DvdNumberText)
            && !int.TryParse(DvdNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            DvdNumberError = "DVD number must be a whole number.";
        }
        else if (ShowDvdNumber && int.TryParse(DvdNumberText, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out var parsedDvdNumber))
        {
            dvdNumber = parsedDvdNumber;
        }

        var sizeBytes = 0L;
        if (!string.IsNullOrWhiteSpace(SizeBytesText)
            && !long.TryParse(SizeBytesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out sizeBytes))
        {
            SizeBytesError = "Size must be a whole number of bytes.";
        }

        var input = new GameRecordInput(
            Title,
            SelectedMedium?.Value ?? (GameMedium)(-1),
            dvdNumber,
            Location,
            SelectedFormat?.Value ?? (GameFormat)(-1),
            sizeBytes,
            _loadedNotesWereNull && Notes.Length == 0 ? null : Notes);

        foreach (var error in GameRecordRules.Validate(input))
        {
            switch (error.Field)
            {
                case nameof(GameRecordInput.Title): TitleError = error.Message; break;
                case nameof(GameRecordInput.Medium): MediumError = error.Message; break;
                case nameof(GameRecordInput.DvdNumber) when DvdNumberError is null:
                    DvdNumberError = error.Message;
                    break;
                case nameof(GameRecordInput.Location): LocationError = error.Message; break;
                case nameof(GameRecordInput.Format): FormatError = error.Message; break;
                case nameof(GameRecordInput.SizeBytes) when SizeBytesError is null:
                    SizeBytesError = error.Message;
                    break;
                case nameof(GameRecordInput.Notes): NotesError = error.Message; break;
            }
        }

        return HasAnyFieldError ? null : input;
    }

    public void Load(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _hasCheckedFields = false;
        _loadedNotesWereNull = record.Notes is null;
        Title = record.Title;
        SelectedMedium = MediumChoices.Single(choice => choice.Value == record.Medium);
        DvdNumberText = record.DvdNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Location = record.Location;
        SelectedFormat = FormatChoices.Single(choice => choice.Value == record.Format);
        SizeBytesText = record.SizeBytes.ToString(CultureInfo.InvariantCulture);
        Notes = record.Notes ?? string.Empty;
        ClearErrors();
    }

    public void Reset()
    {
        _hasCheckedFields = false;
        _loadedNotesWereNull = false;
        Title = string.Empty;
        SelectedMedium = null;
        DvdNumberText = string.Empty;
        Location = string.Empty;
        SelectedFormat = null;
        SizeBytesText = string.Empty;
        Notes = string.Empty;
        ClearErrors();
    }

    private void RecheckFieldsIfNeeded()
    {
        if (_hasCheckedFields)
        {
            ValidateFields();
        }
    }

    private void ClearErrors()
    {
        TitleError = null;
        MediumError = null;
        DvdNumberError = null;
        LocationError = null;
        FormatError = null;
        SizeBytesError = null;
        NotesError = null;
    }
}
