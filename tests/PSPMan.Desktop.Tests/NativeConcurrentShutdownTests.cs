using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Headless;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using PSPMan.Desktop.Views;
using PSPMan.Infraestructure.Devices;
using PSPMan.Infraestructure.Transfers;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

[Collection("Headless UI")]
public sealed class NativeConcurrentShutdownTests
{
    [LinuxNativeCloseTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NativeCallbacksAndSharedFileWaitsDrainBeforeDetachmentAndProviderDisposal(bool sameFile, bool programmatic)
    {
        using var ui = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await ui.DispatchAsync(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "pspman-t19-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "source"); var destination = Path.Combine(root, "psp");
            Directory.CreateDirectory(source); Directory.CreateDirectory(Path.Combine(destination, "ISO"));
            var payload = RandomNumberGenerator.GetBytes(256 * 1024);
            void Zip(string name, string entry)
            {
                using var archive = ZipFile.Open(Path.Combine(source, name), ZipArchiveMode.Create);
                using var output = archive.CreateEntry(entry).Open(); output.Write(payload);
            }
            Zip("a.zip", "a.iso"); Zip("b.zip", "b.iso");
            File.WriteAllBytes(Path.Combine(destination, "ISO/previous.iso"), [7, 8]);
            var hashes = Directory.GetFiles(source).Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
            DeviceSnapshot AtRoot(DeviceSnapshot device, string path) => device with
            { Volumes = device.Volumes.Select(volume => volume with { Root = path }).ToImmutableArray() };
            var provider = new Provider { Inventory = new(1, [AtRoot(Device("source"), source), AtRoot(Device("psp0", true), destination)]) };
            var reader = new PausedNativeReader();
            var main = new MainViewModel(); var window = new MainWindow { DataContext = main, Width = 900, Height = 450 };
            CopyStartup? startup = null; var exits = 0;
            await using var lifetime = new ApplicationDeviceLifetime(window, main, code => { Assert.Equal(0, code); ++exits; window.Hide(); },
                _ => ValueTask.FromResult(new DeviceProviderFactoryResult([provider])),
                (session, error) => startup = new(session, error, archiveReader: reader, syntax: MountedPathSyntax.Linux));
            try
            {
                window.Show(); await lifetime.Initialization;
                await Wait(() => main.Devices!.RemovableSource.Candidates.Length == 1);
                await main.Devices!.ChooseSourceCommand.ExecuteAsync(main.Devices.RemovableSource.Candidates[0]);
                var game = new GameRecord(Guid.NewGuid(), "Native A", GameMedium.Usb, null, "/a.zip", GameFormat.Iso, payload.Length, null);
                var first = main.Copy!.Submit([new(Guid.NewGuid(), game)]).Accepted.Single();
                await Wait(() => Volatile.Read(ref reader.BlockedWrites) == 1);
                var second = main.Copy.Submit([new(Guid.NewGuid(), game with { Id = Guid.NewGuid(), Title = "Native B", Location = sameFile ? "/a.zip" : "/b.zip" })]).Accepted.Single();
                await Wait(() => sameFile ? main.Copy.Operations.Any(row => row.WaitText.Length != 0) : Volatile.Read(ref reader.BlockedWrites) == 2);
                provider.OnDispose = () =>
                {
                    Assert.Equal(2, Volatile.Read(ref reader.DisposedSessions));
                    Assert.Equal(2, Volatile.Read(ref reader.ReturnedExtractions));
                    Assert.Null(main.Copy); Assert.Null(main.Devices);
                    AssertNoDescriptors(root);
                    return ValueTask.CompletedTask;
                };
                Task cleanup;
                if (programmatic) cleanup = lifetime.DisposeAsync().AsTask();
                else
                {
                    window.Close(); var old = main.Copy.CloseConfirmation!;
                    Assert.False(startup!.Coordinator!.IsAdmissionOpen);
                    old.KeepOpenCommand.Execute(null); await lifetime.ShutdownCompletion;
                    Assert.Equal(0, reader.DisposedSessions); Assert.Equal(0, exits);
                    Assert.True(startup.Coordinator.IsAdmissionOpen);
                    window.Close(); main.Copy.CloseConfirmation!.AcceptCommand.Execute(null);
                    old.KeepOpenCommand.Execute(null);
                    cleanup = lifetime.ShutdownCompletion;
                }
                await Task.Delay(30);
                Assert.False(cleanup.IsCompleted); Assert.Equal(0, provider.Disposals);
                Assert.NotNull(main.Copy); Assert.NotNull(main.Devices);
                Assert.False(startup!.Coordinator!.IsAdmissionOpen);
                reader.Release.TrySetResult();
                await cleanup.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal(GameCopyState.Cancelled, (await startup.ViewModel.GetCompletion(first.OperationId)).State);
                Assert.Equal(GameCopyState.Cancelled, (await startup.ViewModel.GetCompletion(second.OperationId)).State);
                Assert.Equal(programmatic ? 0 : 1, exits); Assert.Equal(1, provider.Disposals);
                Assert.Equal(payload[..1], File.ReadAllBytes(Path.Combine(destination, "ISO/a.iso")));
                if (!sameFile) Assert.Equal(payload[..1], File.ReadAllBytes(Path.Combine(destination, "ISO/b.iso")));
                Assert.Equal(new byte[] { 7, 8 }, File.ReadAllBytes(Path.Combine(destination, "ISO/previous.iso")));
                Assert.Equal(hashes, Directory.GetFiles(source).Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray());
                AssertNoDescriptors(root);
            }
            finally
            {
                reader.Release.TrySetResult(); await lifetime.DisposeAsync(); window.Close(); Directory.Delete(root, true);
            }
        });
    }

    private static void AssertNoDescriptors(string root)
    {
        foreach (var path in Directory.EnumerateFiles("/proc/self/fd"))
        {
            var target = new FileInfo(path).LinkTarget;
            Assert.False(target?.StartsWith(root + "/", StringComparison.Ordinal) == true, target);
        }
    }

    // Pauses a real native output callback after one accounted byte. No simulated decoder or filesystem.
    private sealed class PausedNativeReader : IGameArchiveReader
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int BlockedWrites, ReturnedExtractions, DisposedSessions;
        public async ValueTask<IGameArchiveSession> OpenAsync(IGameArchiveSource source, string? password, CancellationToken token)
            => new Session(await new SevenZipGameArchiveReader().OpenAsync(source, password, token), this);
        private sealed class Session(IGameArchiveSession inner, PausedNativeReader owner) : IGameArchiveSession
        {
            public ValueTask<GameArchiveInspection> InspectAsync(CancellationToken token) => inner.InspectAsync(token);
            public async ValueTask ExtractAsync(ImmutableArray<int> indices, IGameArchiveExtractionTarget target,
                IProgress<ArchiveByteProgress>? progress, CancellationToken token)
            {
                try { await inner.ExtractAsync(indices, new Target(target, owner), progress, token); }
                finally { Interlocked.Increment(ref owner.ReturnedExtractions); }
            }
            public async ValueTask DisposeAsync() { await inner.DisposeAsync(); Interlocked.Increment(ref owner.DisposedSessions); }
        }
        private sealed class Target(IGameArchiveExtractionTarget inner, PausedNativeReader owner) : IGameArchiveExtractionTarget
        {
            public async ValueTask<ArchiveEntryTarget> PrepareEntryAsync(GameArchiveEntry entry, CancellationToken token)
            {
                var result = await inner.PrepareEntryAsync(entry, token);
                return result.Output is null ? result : result with { Output = new PausedStream(result.Output, owner, token) };
            }
            public ValueTask CompleteEntryAsync(int index, GameCopyError? error) => inner.CompleteEntryAsync(index, error);
        }
        private sealed class PausedStream(Stream inner, PausedNativeReader owner, CancellationToken operationToken) : Stream
        {
            public override bool CanRead => false;
            public override bool CanWrite => true;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
            {
                await inner.WriteAsync(bytes[..1], token);
                Interlocked.Increment(ref owner.BlockedWrites);
                await owner.Release.Task; // Deliberately noncooperative: native callback still owns output.
                operationToken.ThrowIfCancellationRequested();
                await inner.WriteAsync(bytes[1..], token);
            }
            public override void Write(byte[] bytes, int offset, int count) => WriteAsync(bytes.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}

public sealed class LinuxNativeCloseTheoryAttribute : TheoryAttribute
{
    public LinuxNativeCloseTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Requires the packaged linux-x64 archive bridge and native filesystem; Windows acceptance remains pending.";
    }
}
