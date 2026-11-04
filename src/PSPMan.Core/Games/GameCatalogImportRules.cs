using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PSPMan.Core.Games;

public sealed class GameCatalogCsvHeaderMap
{
    internal GameCatalogCsvHeaderMap(int columnCount, Dictionary<string, int> indices)
    {
        ColumnCount = columnCount;
        Indices = new ReadOnlyDictionary<string, int>(indices);
    }

    public int ColumnCount { get; }

    public IReadOnlyDictionary<string, int> Indices { get; }
}

public static class GameCatalogImportRules
{
    public static bool TryParseMedium(
        string text, out GameMedium medium,
        [NotNullWhen(false)] out GameRecordValidationError? error)
    {
        var success = TryParseLabel(text, GameCatalogCsvSchema.GetMediumLabel, out medium);
        error = success ? null : new(nameof(GameRecordInput.Medium),
            "Medium must be Dvd, Usb, or MemoryCard without whitespace.");
        return success;
    }

    public static bool TryParseFormat(
        string text, out GameFormat format,
        [NotNullWhen(false)] out GameRecordValidationError? error)
    {
        var success = TryParseLabel(text, GameCatalogCsvSchema.GetFormatLabel, out format);
        error = success ? null : new(nameof(GameRecordInput.Format),
            "Format must be ISO or CHD without whitespace.");
        return success;
    }

    public static bool TryParseDvdNumber(
        string text, out int? dvdNumber,
        [NotNullWhen(false)] out GameRecordValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        dvdNumber = null;
        error = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            dvdNumber = GameRecordRules.NormalizeDvdNumber(parsed);
            return true;
        }

        error = new(nameof(GameRecordInput.DvdNumber),
            "DvdNumber must be empty or an integer from 0 to 2147483647 using digits only.");
        return false;
    }

    public static bool TryParseSizeBytes(
        string text, out long sizeBytes,
        [NotNullWhen(false)] out GameRecordValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out sizeBytes) && sizeBytes > 0)
        {
            error = null;
            return true;
        }

        error = new(nameof(GameRecordInput.SizeBytes),
            "SizeBytes must be an integer from 1 to 9223372036854775807 using digits only.");
        return false;
    }

    private static bool TryParseLabel<T>(string text, Func<T, string> getLabel, out T value)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(text, getLabel(candidate), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool TryMapHeadings(
        IReadOnlyList<string> headings,
        [NotNullWhen(true)] out GameCatalogCsvHeaderMap? map,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(headings);
        map = null;
        error = null;
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            if (heading.Any(char.IsWhiteSpace))
            {
                error = $"CSV heading at column {index + 1} must not contain whitespace.";
                return false;
            }

            var canonicalHeading = GameCatalogCsvSchema.Headings.FirstOrDefault(required =>
                string.Equals(required, heading, StringComparison.OrdinalIgnoreCase));
            if (canonicalHeading is not null)
            {
                indices[canonicalHeading] = index;
            }
        }

        var missing = GameCatalogCsvSchema.Headings.Where(heading => !indices.ContainsKey(heading)).ToArray();
        if (missing.Length > 0)
        {
            error = $"Missing required CSV headings: {string.Join(", ", missing)}.";
            return false;
        }

        map = new GameCatalogCsvHeaderMap(headings.Count, indices);
        return true;
    }
}
