using System;
using System.Threading.Tasks;
using PSPMan.Core.Devices;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Desktop;

/// <summary>Owns copy presentation/coordinator, never its caller-owned device session.</summary>
public sealed class CopyStartup : IAsyncDisposable
{
    public CopyStartup(DeviceSessionService? session, string? startupError = null,
        IGameCopyFileSystem? fileSystem = null, IGameArchiveReader? archiveReader = null,
        MountedPathSyntax? syntax = null, Action<Action>? postToUi = null)
    {
        ViewModel = new(postToUi, startupError);
        if (session is null) return;
        fileSystem ??= new FileGameCopyFileSystem(token =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(session.Snapshot.Inventory);
        });
        archiveReader ??= new SevenZipGameArchiveReader();
        Coordinator = new(session, new GameCopyService(fileSystem, archiveReader, ViewModel),
            syntax ?? (OperatingSystem.IsWindows() ? MountedPathSyntax.Windows : MountedPathSyntax.Linux));
        ViewModel.AttachCoordinator(Coordinator);
    }
    public GameCopyViewModel ViewModel { get; }
    public GameCopySessionCoordinator? Coordinator { get; }
    public ValueTask DisposeAsync() => ViewModel.DisposeAsync();
}
