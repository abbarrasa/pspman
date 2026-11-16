using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PSPMan.Core.Transfers;

namespace PSPMan.Infraestructure.Transfers;

public sealed class ArchiveRuntimeResolver(string? applicationDirectory = null)
{
    internal const string SourceHash = "a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4";
    public static string CurrentRuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;

    internal RuntimeAssets Resolve(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var app = applicationDirectory ?? AppContext.BaseDirectory;
            if (!Path.IsPathFullyQualified(app)) throw new IOException("Runtime application directory must be absolute.");
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            var root = Path.GetFullPath(Path.Combine(app, "runtimes", CurrentRuntimeIdentifier, "native"));
            RejectLinks(root);
            var manifestPath = Path.Combine(root, "runtime.json");
            RejectLinks(manifestPath);
            var manifest = JsonSerializer.Deserialize<RuntimeManifest>(File.ReadAllText(manifestPath))
                ?? throw new IOException("Missing native runtime manifest.");
            if (manifest.Abi != 1 || manifest.Version != 2606 || manifest.Rid != CurrentRuntimeIdentifier
                || manifest.SourceSha256 != SourceHash || manifest.Files is null)
                throw new IOException("Native runtime version, ABI, architecture or source mismatch.");
            var bridge = OperatingSystem.IsWindows() ? "pspman_archive_bridge.dll" : "libpspman_archive_bridge.so";
            var engine = OperatingSystem.IsWindows() ? "7z.dll" : "7z.so";
            string[] required = [bridge, engine, "7z2606-src.tar.xz", "LICENSE.7zip.txt", "COPYING.LGPL.txt",
                "LICENSE.unRAR.txt", "LICENSE.PSPMan.txt", "bridge-source/bridge.cpp", "bridge-source/bridge.h",
                "bridge-source/bridge.def", "bridge-source/build-linux.sh", "bridge-source/build-windows.ps1",
                "bridge-source/README.md", "bridge-source/source.lock.json", "bridge-source/ArchiveRuntime.targets",
                "bridge-source/package-linux.sh", "bridge-source/package-windows.ps1"];
            foreach (var name in required)
                if (!manifest.Files.ContainsKey(name)) throw new IOException("Incomplete native runtime package.");
            if (manifest.Files.Count != required.Length) throw new IOException("Unexpected native runtime assets.");
            if (manifest.Files["7z2606-src.tar.xz"] != SourceHash) throw new IOException("Mismatched engine source archive.");
            foreach (var (name, hash) in manifest.Files)
            {
                if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':')))
                    throw new IOException("Invalid runtime asset name.");
                var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
                RejectLinks(path);
                using var stream = File.OpenRead(path);
                if (!Convert.ToHexStringLower(SHA256.HashDataAsync(stream, cancellationToken).AsTask().GetAwaiter().GetResult()).Equals(hash, StringComparison.Ordinal))
                    throw new IOException("Native runtime asset integrity mismatch.");
            }
            return new(Path.Combine(root, bridge), Path.Combine(root, engine));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or PlatformNotSupportedException or ArgumentException)
        {
            throw new GameCopyException(new(GameCopyErrorCode.UnsupportedArchive,
                "The bundled native archive runtime is missing, incompatible or damaged."), error);
        }
    }
    private static void RejectLinks(string path)
    {
        FileSystemInfo? item = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        while (item is not null)
        {
            if (item.LinkTarget is not null) throw new IOException("Native runtime paths cannot use links.");
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent;
        }
    }
    internal sealed record RuntimeAssets(string Bridge, string Engine);
    internal sealed record RuntimeManifest(int Abi, int Version, string Rid, string SourceSha256, Dictionary<string, string> Files);
}
