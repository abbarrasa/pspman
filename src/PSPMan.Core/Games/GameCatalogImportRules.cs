using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

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
