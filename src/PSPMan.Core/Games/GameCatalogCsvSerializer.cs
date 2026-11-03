using System.Globalization;
using System.Text;

namespace PSPMan.Core.Games;

public static class GameCatalogCsvSerializer
{
    public static byte[] SerializeEmptyCatalog()
    {
        return SerializeCatalog([]);
    }

    public static byte[] SerializeCatalog(IEnumerable<GameRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return SerializeRows(records.Select(ProjectRecord));
    }

    internal static byte[] SerializeRows(IEnumerable<string[]> rows)
    {
        var csv = new StringBuilder(GameCatalogCsvSchema.HeaderLine);
        foreach (var fields in rows)
        {
            csv.AppendJoin(';', fields.Select((field, index) =>
                EscapeCsvField(index is 2 or 5 ? field : ProtectExcelText(field)))).Append("\r\n");
        }

        return [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string[] ProjectRecord(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return
        [
            record.Title,
            GameCatalogCsvSchema.GetMediumLabel(record.Medium),
            record.Medium == GameMedium.Dvd
                ? record.DvdNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty,
            record.Location,
            GameCatalogCsvSchema.GetFormatLabel(record.Format),
            record.SizeBytes.ToString(CultureInfo.InvariantCulture),
            record.Notes ?? string.Empty
        ];
    }

    private static bool NeedsExcelTextProtection(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                return character is '=' or '+' or '-' or '@';
            }
        }

        return false;
    }

    private static string ProtectExcelText(string text)
    {
        return NeedsExcelTextProtection(text) ? $"\t{text}" : text;
    }

    private static string EscapeCsvField(string field)
    {
        return field.IndexOfAny([';', '"', '\r', '\n', '\t']) >= 0
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }
}
