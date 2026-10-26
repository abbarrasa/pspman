namespace PSPMan.Core.Games;

public sealed record GameRecordValidationError(string Field, string Message);

public static class GameRecordRules
{
    public static int? NormalizeDvdNumber(int? dvdNumber) => dvdNumber == 0 ? null : dvdNumber;

    public static bool HasSameIdentity(GameRecordInput candidate, GameRecord existing)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);

        return candidate.Medium == existing.Medium
            && string.Equals(candidate.Location, existing.Location, StringComparison.Ordinal)
            && (candidate.Medium != GameMedium.Dvd
                || NormalizeDvdNumber(candidate.DvdNumber) == NormalizeDvdNumber(existing.DvdNumber));
    }

    public static IReadOnlyList<GameRecordValidationError> Validate(GameRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<GameRecordValidationError>();

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors.Add(new(nameof(GameRecordInput.Title), "Title is required."));
        }

        if (string.IsNullOrWhiteSpace(input.Location))
        {
            errors.Add(new(nameof(GameRecordInput.Location), "Location is required."));
        }
        else if (!IsWellFormedLocation(input.Location))
        {
            errors.Add(new(nameof(GameRecordInput.Location), "Location must be a complete path from the medium root."));
        }

        if (!Enum.IsDefined(input.Medium))
        {
            errors.Add(new(nameof(GameRecordInput.Medium), "Select a supported medium."));
        }

        if (!Enum.IsDefined(input.Format))
        {
            errors.Add(new(nameof(GameRecordInput.Format), "Select a supported format."));
        }

        var dvdNumber = NormalizeDvdNumber(input.DvdNumber);

        if (input.Medium == GameMedium.Dvd && dvdNumber is null or <= 0)
        {
            errors.Add(new(nameof(GameRecordInput.DvdNumber), "DVD number must be a positive integer."));
        }
        else if (input.Medium != GameMedium.Dvd && Enum.IsDefined(input.Medium) && dvdNumber is not null)
        {
            errors.Add(new(nameof(GameRecordInput.DvdNumber), "DVD number is only allowed for DVD records."));
        }

        if (input.SizeBytes <= 0)
        {
            errors.Add(new(nameof(GameRecordInput.SizeBytes), "Size must be a positive byte count."));
        }

        if (input.Notes?.Length > 2_500)
        {
            errors.Add(new(nameof(GameRecordInput.Notes), "Notes cannot exceed 2,500 UTF-16 code units."));
        }

        return errors;
    }

    private static bool IsWellFormedLocation(string location)
    {
        if (location[0] != '/' || location.Contains('\\') || location.Any(char.IsControl))
        {
            return false;
        }

        return location[1..].Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }
}
