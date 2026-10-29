using System.Globalization;
using System.Text;

namespace PSPMan.Core.Games;

public static class GameCatalogCsvSerializer
{
    private const string HeaderLine =
        "Title;Medium;DVD Number;Location;Format;Decompressed Size (bytes);Approximate Size (binary units);Notes\r\n";

    public static byte[] SerializeEmptyCatalog()
    {
        return SerializeCatalog([]);
    }

    public static byte[] SerializeCatalog(IEnumerable<GameRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var csv = new StringBuilder(HeaderLine);
        foreach (var record in records)
        {
            var fields = ProjectRecord(record);
            csv.AppendJoin(';', fields.Select(EscapeCsvField)).Append("\r\n");
        }

        return [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string[] ProjectRecord(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var medium = record.Medium switch
        {
            GameMedium.Dvd => "DVD",
            GameMedium.Usb => "USB",
            GameMedium.MemoryCard => "Memory Card",
            _ => throw new ArgumentOutOfRangeException(nameof(record), "Unsupported game medium.")
        };
        var format = record.Format switch
        {
            GameFormat.Iso => "ISO",
            GameFormat.Chd => "CHD",
            _ => throw new ArgumentOutOfRangeException(nameof(record), "Unsupported game format.")
        };

        return
        [
            record.Title,
            medium,
            record.Medium == GameMedium.Dvd
                ? record.DvdNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty,
            record.Location,
            format,
            record.SizeBytes.ToString(CultureInfo.InvariantCulture),
            ByteSizeFormatter.Format(record.SizeBytes),
            record.Notes ?? string.Empty
        ];
    }

    private static string EscapeCsvField(string field)
    {
        return field.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }
}
