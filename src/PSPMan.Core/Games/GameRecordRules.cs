namespace PSPMan.Core.Games;

public sealed record GameRecordValidationError(string Field, string Message);

public static class GameRecordRules
{
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

        if (!Enum.IsDefined(input.Medium))
        {
            errors.Add(new(nameof(GameRecordInput.Medium), "Select a supported medium."));
        }

        if (!Enum.IsDefined(input.Format))
        {
            errors.Add(new(nameof(GameRecordInput.Format), "Select a supported format."));
        }

        return errors;
    }
}
