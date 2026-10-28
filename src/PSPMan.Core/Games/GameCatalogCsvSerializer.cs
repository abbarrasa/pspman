using System.Text;

namespace PSPMan.Core.Games;

public static class GameCatalogCsvSerializer
{
    private const string HeaderLine =
        "Title;Medium;DVD Number;Location;Format;Decompressed Size (bytes);Approximate Size (binary units);Notes\r\n";

    public static byte[] SerializeEmptyCatalog()
    {
        return [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(HeaderLine)];
    }
}
