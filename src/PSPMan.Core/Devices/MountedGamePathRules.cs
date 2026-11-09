using PSPMan.Core.Games;

namespace PSPMan.Core.Devices;

public enum MountedPathSyntax
{
    Linux,
    Windows
}

/// <summary>A constructed path is not proof of file existence or access.</summary>
public sealed record MountedGamePathResult(string? Path, string? Error = null);

public static class MountedGamePathRules
{
    /// <summary>Combines a trusted observed volume root with the complete catalog Location without I/O.</summary>
    public static MountedGamePathResult Resolve(string? root, string? location, MountedPathSyntax syntax)
    {
        if (syntax is not (MountedPathSyntax.Linux or MountedPathSyntax.Windows))
        {
            return new MountedGamePathResult(null, "Unsupported mount path syntax.");
        }

        if (!GameRecordRules.IsWellFormedLocation(location))
        {
            return new MountedGamePathResult(null, "Location must be a complete path from the medium root.");
        }

        if (syntax == MountedPathSyntax.Windows)
        {
            return ResolveWindows(root, location!);
        }

        if (!IsWellFormedLinuxRoot(root))
        {
            return new MountedGamePathResult(null, "The mounted volume root is not a valid absolute Linux path.");
        }

        // Remove only the root's optional final separator. Location's leading slash joins below it;
        // passing that slash to a host-dependent path combiner could discard the selected root.
        var prefix = root!.EndsWith('/') ? root[..^1] : root;
        return new MountedGamePathResult(prefix + location);
    }

    private static MountedGamePathResult ResolveWindows(string? root, string location)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length < 3
            || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
        {
            return new MountedGamePathResult(null, "The mounted volume root must be an absolute Windows drive path.");
        }

        var prefix = root.EndsWith('\\') ? root[..^1] : root;
        if (prefix.Length > 2 && !prefix[3..].Split('\\').All(IsValidWindowsSegment))
        {
            return new MountedGamePathResult(null, "The mounted volume root contains an invalid Windows name.");
        }

        if (!location[1..].Split('/').All(IsValidWindowsSegment))
        {
            return new MountedGamePathResult(null, "Location contains a name that cannot be used on Windows.");
        }

        return new MountedGamePathResult(prefix + location.Replace('/', '\\'));
    }

    private static bool IsValidWindowsSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
        {
            return false;
        }

        // DOS device names remain reserved even when followed by a file extension.
        var stem = segment.Split('.', 2)[0].TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'));
    }

    private static bool IsWellFormedLinuxRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || root[0] != '/' || root.Contains('\0'))
        {
            return false;
        }

        if (root == "/")
        {
            return true;
        }

        var path = root.EndsWith('/') ? root[..^1] : root;
        return path[1..].Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }
}
