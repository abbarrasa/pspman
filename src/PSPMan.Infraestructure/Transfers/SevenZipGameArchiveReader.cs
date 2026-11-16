using PSPMan.Core.Transfers;
namespace PSPMan.Infraestructure.Transfers;

public sealed class SevenZipGameArchiveReader(string? applicationDirectory = null) : IGameArchiveReader
{
    public async ValueTask<IGameArchiveSession> OpenAsync(IGameArchiveSource source, string? password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var assets = await Task.Run(() => new ArchiveRuntimeResolver(applicationDirectory).Resolve(cancellationToken), cancellationToken).ConfigureAwait(false);
        var session = new SevenZipNativeSession(source, password, assets);
        try { await session.OpenAsync(cancellationToken).ConfigureAwait(false); return session; }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
