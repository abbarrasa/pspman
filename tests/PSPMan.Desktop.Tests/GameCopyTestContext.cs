using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalonia.Threading;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

/// <summary>Real session/engine/coordinator with controlled byte I/O; no native device claims.</summary>
internal sealed class GameCopyTestContext : IAsyncDisposable
{
    internal readonly GameRecord Game = new(Guid.NewGuid(), "Test game", GameMedium.Usb, null, "/game.zip", GameFormat.Iso, 999, null);
    internal readonly CopyIo IO = new();
    internal readonly Provider Devices = new();
    internal DeviceSessionService Session = null!;
    internal CopyStartup Startup = null!;
    internal MainViewModel Main = null!;
    internal Repository Catalog = null!;
    internal GameCopyViewModel Copy => Startup.ViewModel;
    internal DevicesViewModel DeviceModel = null!;
    internal static DeviceSnapshot Device(string name, bool psp = false)
    {
        var key = new DeviceKey("copy-ui", name);
        var identity = new DeviceIdentity(name, DeviceIdentityConfidence.Persistent);
        return new(key, name, psp ? DeviceMedium.Psp : DeviceMedium.Usb,
            DeviceCapabilities.SafeRemoval | (psp ? DeviceCapabilities.PspDestination : DeviceCapabilities.RemovableSource),
            identity, "g1", DeviceState.Ready, [new(new(key, "volume"), name, "/" + name, identity, DeviceState.Ready)]);
    }
    internal static async Task<GameCopyTestContext> Create(int psps = 1, Action<Action>? post = null, DeviceInventorySnapshot? inventory = null)
    {
        var f = new GameCopyTestContext();
        f.Devices.Inventory = inventory ?? new(1, [Device("source"), Device("alternate"), .. Enumerable.Range(0, psps).Select(i => Device("psp" + i, true))]);
        f.Session = new([f.Devices]);
        await f.Session.StartAsync();
        await f.Session.SetAssociationsAsync(DeviceAssociationRules.ChooseVolume(new(), SourceGroup.Removable, new(new("copy-ui", "source"), "volume")));
        f.Startup = new(f.Session, fileSystem: f.IO, archiveReader: f.IO, syntax: MountedPathSyntax.Linux, postToUi: post,
            resources: new TestCopyResources());
        f.DeviceModel = new(f.Session, removalInterceptor: f.Startup.Coordinator);
        f.Catalog = new(f.Game);
        f.Main = new(new GameCatalogService(f.Catalog), null, devicesViewModel: f.DeviceModel, mountedPathSyntax: MountedPathSyntax.Linux);
        f.Main.AttachCopy(f.Copy); f.Main.SelectedGame = f.Game;
        return f;
    }
    internal static async Task Wait(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(5, timeout.Token); }
    }

    // Controlled UI I/O only. Production composition always uses native CopyDestinationResources.
    internal sealed class TestCopyResources : IGameCopyResources
    {
        public IGameCopyDestination BindDestination(Guid id, CopyVolumeEvidence volume, IGameCopyDestination destination) => destination;
        public async ValueTask<IGameCopyBudgetLease> ReserveAsync(Guid id, CopyVolumeEvidence volume,
            IGameCopyDestination destination, long pending, CancellationToken token)
        {
            var lease = new Budget(id, volume, destination);
            await lease.RevalidateAsync(pending, token); return lease;
        }
        public ValueTask<IGameCopyEntryLease> AcquireEntryAsync(Guid id, CopyVolumeEvidence volume, IGameCopyDestination destination,
            ImmutableArray<string> segments, bool directory, IProgress<GameCopyResourceWait>? waiting, CancellationToken token)
            => ValueTask.FromResult<IGameCopyEntryLease>(new Entry(id, volume));
        private sealed class Entry(Guid id, CopyVolumeEvidence volume) : IGameCopyEntryLease
        {
            public Guid OperationId => id;
            public CopyVolumeKey Volume => CopyVolumeKey.From(volume);
            public string TargetIdentity => "controlled";
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private sealed class Budget(Guid id, CopyVolumeEvidence volume, IGameCopyDestination destination) : IGameCopyBudgetLease
        {
            public Guid OperationId => id;
            public CopyVolumeKey Volume => CopyVolumeKey.From(volume);
            public async ValueTask RevalidateAsync(long bytes, CancellationToken token)
            {
                var error = GameCopyRules.CheckCapacity(bytes, await destination.GetAvailableBytesAsync(token));
                if (error is not null) throw new GameCopyException(error);
            }
            public ValueTask SkipPendingAsync(long bytes, CancellationToken token) => ValueTask.CompletedTask;
            public ValueTask<IGameCopyWriteLease> BeginWriteAsync(long bytes, CancellationToken token) => throw new NotSupportedException();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    public async ValueTask DisposeAsync()
    { Main.AttachCopy(null); await Startup.DisposeAsync(); DeviceModel.Dispose(); await Session.DisposeAsync(); }

    internal sealed class Repository(GameRecord game) : IGameRecordRepository
    {
        internal int Writes;
        public IReadOnlyList<GameRecord> List() => [game];
        public GameRecord? GetById(Guid id) => id == game.Id ? game : null;
        public void Create(GameRecord record) { ++Writes; throw new InvalidOperationException(); }
        public void Update(GameRecord record) { ++Writes; throw new InvalidOperationException(); }
        public void Delete(Guid id) { ++Writes; throw new InvalidOperationException(); }
    }

    internal sealed class Provider : IDeviceProvider
    {
        internal int Disposals, Reads;
        internal Func<ValueTask>? OnDispose;
        internal Func<CancellationToken, ValueTask<DeviceOperationResult>>? OnRemove;
        internal int Removals;
        internal DeviceInventorySnapshot Inventory = new(0, []);
        private readonly Channel<DeviceInventorySnapshot> _changes = Channel.CreateUnbounded<DeviceInventorySnapshot>();
        internal void Publish(DeviceInventorySnapshot inventory) { Inventory = inventory; _changes.Writer.TryWrite(inventory); }
        public string ProviderId => "copy-ui";
        public ValueTask<DeviceInventorySnapshot> GetSnapshotAsync(CancellationToken token = default) { ++Reads; return new(Inventory); }
        public async IAsyncEnumerable<DeviceInventorySnapshot> WatchChangesAsync([EnumeratorCancellation] CancellationToken token = default)
        { await foreach (var value in _changes.Reader.ReadAllAsync(token)) yield return value; }
        public ValueTask<DeviceOperationResult> MountAsync(DeviceMountRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<DeviceOperationResult> RemoveAsync(DeviceRemovalRequest request, CancellationToken token = default)
        { ++Removals; return OnRemove?.Invoke(token) ?? ValueTask.FromResult(new DeviceOperationResult(DeviceOperationStatus.Failed, new("Veto", "Native removal veto"))); }
        public async ValueTask DisposeAsync() { ++Disposals; if (OnDispose is not null) await OnDispose(); }
    }

    internal sealed class CopyIo : IGameCopyFileSystem, IGameArchiveSource, IGameCopyDestination, IGameArchiveReader
    {
        internal ImmutableArray<GameArchiveEntry> Entries = [new(0, "game.iso", 3)];
        internal readonly Dictionary<string, MemoryStream> Files = [];
        internal long? Available = 100;
        internal GameCopyErrorCode? OpenErrorOnce, ExtractErrorOnce;
        internal Func<CancellationToken, Task>? Pause;
        internal Func<Task>? ClosePause;
        internal bool ChangeOnce, OutputClosed = true, Closed, RanOnUi;
        internal int Opens;
        public string ArchiveName => "game.zip";
        public string DestinationPath => "/psp0/ISO";
        public ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence source, string location, CancellationToken token)
        { RanOnUi = Dispatcher.UIThread.CheckAccess(); ++Opens; return ValueTask.FromResult<IGameArchiveSource>(this); }
        public ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence destination, CancellationToken token) => ValueTask.FromResult<IGameCopyDestination>(this);
        public ValueTask<string> CaptureEvidenceAsync(CancellationToken token) => new("evidence");
        public ValueTask<Stream> OpenPartAsync(string name, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateAsync(string expectedEvidence, CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask ValidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask<long?> GetAvailableBytesAsync(CancellationToken token) => new(Available);
        public ValueTask<IGameArchiveSession> OpenAsync(IGameArchiveSource source, string? password, CancellationToken token)
        {
            if (OpenErrorOnce is { } error) { OpenErrorOnce = null; throw new GameCopyException(new(error, "Open failed")); }
            return ValueTask.FromResult<IGameArchiveSession>(new Archive(this));
        }
        public ValueTask<CopyTargetSnapshot> InspectAsync(ImmutableArray<string> segments, CancellationToken token)
        {
            var name = string.Join('/', segments);
            lock (Files)
                return new(new CopyTargetSnapshot(Files.TryGetValue(name, out var stream) ? CopyTargetKind.File : CopyTargetKind.Missing,
                    stream is null ? null : new(name, stream.Length, "version")));
        }
        public ValueTask<GameCopySkipReason?> EnsureDirectoryAsync(ImmutableArray<string> segments, CancellationToken token) => new((GameCopySkipReason?)null);
        public ValueTask<CopyOutputOpenResult> OpenOutputAsync(ImmutableArray<string> segments, CopyFileEvidence? consent, CancellationToken token)
        {
            if (ChangeOnce) { ChangeOnce = false; return new(new CopyOutputOpenResult(null, TargetChanged: true)); }
            var stream = new MemoryStream();
            lock (Files) Files[string.Join('/', segments)] = stream;
            OutputClosed = false;
            return new(new CopyOutputOpenResult(new OutputFile(this, stream)));
        }
        public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
        private sealed class OutputFile(CopyIo io, MemoryStream stream) : IGameCopyOutputFile
        {
            public Stream Output => stream;
            public async ValueTask DisposeAsync() { if (io.ClosePause is not null) await io.ClosePause(); io.OutputClosed = true; }
        }
        private sealed class Archive(CopyIo io) : IGameArchiveSession
        {
            public ValueTask<GameArchiveInspection> InspectAsync(CancellationToken token) => new(new GameArchiveInspection(io.Entries, "inspection"));
            public async ValueTask ExtractAsync(ImmutableArray<int> indices, IGameArchiveExtractionTarget target, IProgress<ArchiveByteProgress>? progress, CancellationToken token)
            {
                foreach (var index in indices)
                {
                    var entry = io.Entries.Single(e => e.Index == index);
                    var output = await target.PrepareEntryAsync(entry, token);
                    GameCopyError? error = null;
                    try
                    {
                        if (output.Output is null) continue;
                        await output.Output.WriteAsync(new byte[] { 1 }, token);
                        progress?.Report(new(index, 1, 1));
                        if (io.Pause is not null) await io.Pause(token);
                        if (io.ExtractErrorOnce is { } code)
                        { io.ExtractErrorOnce = null; error = new(code, "Decode failed", index); throw new GameCopyException(error); }
                        await output.Output.WriteAsync(new byte[] { 2, 3 }, token);
                        progress?.Report(new(index, 3, 3));
                    }
                    catch (OperationCanceledException) { error = new(GameCopyErrorCode.Cancelled, "Stopped", index); throw; }
                    finally { await target.CompleteEntryAsync(index, error); }
                }
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
