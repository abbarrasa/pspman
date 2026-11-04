using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PSPMan.Core.Games;

// Record equality compares Location strings ordinally and case-sensitively.
public readonly record struct GameCatalogImportIdentity(GameMedium Medium, string Location, int? DvdNumber);

public sealed record GameCatalogImportCandidate(long StartLine, GameRecordInput Input);

public sealed record GameCatalogImportModification(long StartLine, Guid ExistingId, GameRecordInput Input);

public sealed class GameCatalogImportClassification
{
    internal GameCatalogImportClassification(
        List<GameCatalogImportCandidate> newGames,
        List<GameCatalogImportModification> modifiedGames)
    {
        NewGames = newGames.AsReadOnly();
        ModifiedGames = modifiedGames.AsReadOnly();
    }

    public IReadOnlyList<GameCatalogImportCandidate> NewGames { get; }

    public IReadOnlyList<GameCatalogImportModification> ModifiedGames { get; }

    public int NewCount => NewGames.Count;

    public int ModifiedCount => ModifiedGames.Count;
}

public sealed record GameCatalogImportRowError(
    long StartLine,
    string Message,
    IReadOnlyList<GameRecordValidationError> ValidationErrors);

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
    public static GameCatalogImportClassification ClassifyCandidates(
        IReadOnlyDictionary<GameCatalogImportIdentity, GameCatalogImportCandidate> candidates,
        IEnumerable<GameRecord> catalogSnapshot)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(catalogSnapshot);
        var catalogByIdentity = catalogSnapshot.ToDictionary(record =>
            Identity(record.Medium, record.Location, record.DvdNumber));
        var newGames = new List<GameCatalogImportCandidate>();
        var modifiedGames = new List<GameCatalogImportModification>();

        foreach (var candidate in candidates.Values.OrderBy(candidate => candidate.StartLine))
        {
            var input = candidate.Input;
            if (!catalogByIdentity.TryGetValue(Identity(input), out var existing))
            {
                newGames.Add(candidate);
                continue;
            }

            // Identity already covers Medium, Location, and normalized DVD numbering.
            if (string.Equals(input.Title, existing.Title, StringComparison.Ordinal)
                && input.Format == existing.Format
                && input.SizeBytes == existing.SizeBytes
                && string.Equals(input.Notes ?? string.Empty, existing.Notes ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            modifiedGames.Add(new(candidate.StartLine, existing.Id, input));
        }

        return new(newGames, modifiedGames);
    }

    public static GameCatalogImportIdentity Identity(GameRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Identity(input.Medium, input.Location, input.DvdNumber);
    }

    private static GameCatalogImportIdentity Identity(GameMedium medium, string location, int? dvdNumber) =>
        new(medium, location, medium == GameMedium.Dvd ? GameRecordRules.NormalizeDvdNumber(dvdNumber) : null);

    public static bool SelectLastValid(
        GameCatalogCsvRecord record,
        GameCatalogCsvHeaderMap headerMap,
        IDictionary<GameCatalogImportIdentity, GameCatalogImportCandidate> candidates,
        [NotNullWhen(false)] out GameCatalogImportRowError? error)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!TryMapSourceRow(record, headerMap, out var input, out error))
        {
            return false;
        }

        candidates[Identity(input)] = new(record.StartLine, input);
        return true;
    }

    public static bool TryMapSourceRow(
        GameCatalogCsvRecord record,
        GameCatalogCsvHeaderMap headerMap,
        [NotNullWhen(true)] out GameRecordInput? input,
        [NotNullWhen(false)] out GameCatalogImportRowError? error)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(headerMap);
        input = null;
        error = null;
        if (record.Cells.Count < headerMap.ColumnCount)
        {
            error = new(record.StartLine,
                $"Row has {record.Cells.Count} cells; expected at least {headerMap.ColumnCount}.", []);
            return false;
        }

        // Only mapped header indices are read; surplus cells are never interpreted.
        if (TryMapGameRow(record.Cells, headerMap, out input, out var validationErrors))
        {
            return true;
        }

        error = new(record.StartLine,
            string.Join(" ", validationErrors.Select(item => $"{item.Field}: {item.Message}")),
            Array.AsReadOnly(validationErrors.ToArray()));
        return false;
    }

    public static string UndoFormulaProtection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] == '\t')
        {
            foreach (var character in text.AsSpan(1))
            {
                if (!char.IsWhiteSpace(character))
                {
                    return character is '=' or '+' or '-' or '@' ? text[1..] : text;
                }
            }
        }

        return text;
    }

    /// <summary>Maps a complete row; callers must check its width against the header first.</summary>
    public static bool TryMapGameRow(
        IReadOnlyList<string> cells,
        GameCatalogCsvHeaderMap headerMap,
        [NotNullWhen(true)] out GameRecordInput? input,
        out IReadOnlyList<GameRecordValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(headerMap);
        if (cells.Count < headerMap.ColumnCount)
        {
            throw new ArgumentException("The row must contain at least as many cells as its header.", nameof(cells));
        }

        input = null;
        var conversionErrors = new List<GameRecordValidationError>();
        if (!TryParseMedium(cells[headerMap.Indices[nameof(GameRecordInput.Medium)]], out var medium, out var mediumError))
        {
            conversionErrors.Add(mediumError);
        }
        if (!TryParseFormat(cells[headerMap.Indices[nameof(GameRecordInput.Format)]], out var format, out var formatError))
        {
            conversionErrors.Add(formatError);
        }
        if (!TryParseDvdNumber(cells[headerMap.Indices[nameof(GameRecordInput.DvdNumber)]], out var dvdNumber, out var dvdError))
        {
            conversionErrors.Add(dvdError);
        }
        if (!TryParseSizeBytes(cells[headerMap.Indices[nameof(GameRecordInput.SizeBytes)]], out var sizeBytes, out var sizeError))
        {
            conversionErrors.Add(sizeError);
        }

        if (conversionErrors.Count > 0)
        {
            errors = conversionErrors.AsReadOnly();
            return false;
        }

        var candidate = new GameRecordInput(
            UndoFormulaProtection(cells[headerMap.Indices[nameof(GameRecordInput.Title)]]),
            medium,
            dvdNumber,
            UndoFormulaProtection(cells[headerMap.Indices[nameof(GameRecordInput.Location)]]),
            format,
            sizeBytes,
            UndoFormulaProtection(cells[headerMap.Indices[nameof(GameRecordInput.Notes)]]));
        errors = GameRecordRules.Validate(candidate);
        if (errors.Count > 0)
        {
            return false;
        }

        input = candidate;
        return true;
    }

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
