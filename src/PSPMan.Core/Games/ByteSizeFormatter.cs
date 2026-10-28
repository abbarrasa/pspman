using System.Globalization;

namespace PSPMan.Core.Games;

public static class ByteSizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
        var amount = (double)bytes;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return $"{amount.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
