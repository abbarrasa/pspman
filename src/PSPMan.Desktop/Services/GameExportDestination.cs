using System;
using System.IO;
using System.Linq;

namespace PSPMan.Desktop.Services;

public static class GameExportDestination
{
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    public static bool TryResolve(
        string? folderPath,
        string? fileName,
        string? catalogPath,
        out string? destinationPath,
        out string? error)
    {
        destinationPath = null;

        if (string.IsNullOrWhiteSpace(folderPath) || !Path.IsPathFullyQualified(folderPath))
        {
            error = "Choose a local export folder.";
            return false;
        }

        if (!IsValidFileName(fileName))
        {
            error = "Enter a single, valid .csv file name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(catalogPath) || !Path.IsPathFullyQualified(catalogPath))
        {
            error = "The catalog path is unavailable.";
            return false;
        }

        try
        {
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
            var candidate = Path.GetFullPath(Path.Combine(folder, fileName!));
            var catalog = Path.GetFullPath(catalogPath);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!string.Equals(Path.GetDirectoryName(candidate), folder, comparison))
            {
                error = "The export file must stay inside the chosen folder.";
                return false;
            }

            if (string.Equals(candidate, catalog, comparison))
            {
                error = "The export cannot replace the game catalog.";
                return false;
            }

            destinationPath = candidate;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The export destination is invalid.";
            return false;
        }
    }

    private static bool IsValidFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName != fileName.Trim()
            || fileName.Length <= ".csv".Length
            || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            || fileName.Any(character => char.IsControl(character)
                || "<>:\"/\\|?*".Contains(character))
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var stem = fileName[..^".csv".Length];
        if (string.IsNullOrWhiteSpace(stem) || stem is "." or "..")
        {
            return false;
        }

        var deviceName = stem.Split('.')[0];
        return !ReservedDeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase);
    }
}
