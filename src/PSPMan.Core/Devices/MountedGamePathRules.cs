using PSPMan.Core.Games;

namespace PSPMan.Core.Devices;

public enum MountedPathSyntax
{
    Linux
}

/// <summary>A constructed path is not proof of file existence or access.</summary>
public sealed record MountedGamePathResult(string? Path, string? Error = null);

public static class MountedGamePathRules
{
    /// <summary>Combines a trusted observed volume root with the complete catalog Location without I/O.</summary>
    public static MountedGamePathResult Resolve(string? root, string? location, MountedPathSyntax syntax)
    {
        if (syntax != MountedPathSyntax.Linux)
        {
            return new MountedGamePathResult(null, "Unsupported mount path syntax.");
        }

        if (!GameRecordRules.IsWellFormedLocation(location))
        {
            return new MountedGamePathResult(null, "Location must be a complete path from the medium root.");
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
