using System.IO.Compression;
using System.Security.Cryptography;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Actual engine, packaged reader and confined filesystem; hooks control scheduling/faults only.</summary>
public sealed class GameCopyEngineResourceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [LinuxArchiveFileSystemTheory]
    [InlineData("separate-files")]
    [InlineData("separate-psps")]
    [InlineData("same-file")]
    [InlineData("failed-holder")]
    [InlineData("cancelled-holder")]
    [InlineData("cancel-waiter")]
    [InlineData("changed-consent")]
    [InlineData("capacity")]
    [InlineData("skip-capacity")]
    [InlineData("owner-flush-fault")]
    [InlineData("wrong-password-decision")]
    [InlineData("damaged-password-decision")]
    [InlineData("retry-capacity")]
    public Task Linux_engine_resource_matrix(string scenario) => Run(scenario);

    [WindowsCopyTheory]
    [InlineData("separate-files")]
    [InlineData("separate-psps")]
    [InlineData("same-file")]
    [InlineData("failed-holder")]
    [InlineData("cancelled-holder")]
    [InlineData("cancel-waiter")]
    [InlineData("changed-consent")]
    [InlineData("capacity")]
    [InlineData("skip-capacity")]
    [InlineData("owner-flush-fault")]
    [InlineData("wrong-password-decision")]
    [InlineData("damaged-password-decision")]
    [InlineData("retry-capacity")]
    public Task Windows_engine_resource_matrix(string scenario) => Run(scenario);

    private static Task Run(string scenario) => scenario switch
    {
        "separate-files" => Overlap(false),
        "separate-psps" => Overlap(true),
        "capacity" => Capacity(false),
        "skip-capacity" => Capacity(true),
        "owner-flush-fault" => OwnerFault(),
        "wrong-password-decision" => PasswordDecision(false),
        "damaged-password-decision" => PasswordDecision(true),
        "retry-capacity" => RetryCapacity(),
        _ => SameFile(scenario)
    };

    private static async Task Overlap(bool differentPsps)
    {
        using var source = new Area(); using var a = new Area(); using var other = new Area();
        var b = differentPsps ? other : a;
        var payload = Payload(); CreateZip(source.Root, "a.zip", "nested/a.iso", payload);
        CreateZip(source.Root, "b.zip", "nested/b.iso", payload);
        var sourceHash = Hashes(source.Root);
        Existing(a, "nested/a.iso"); Existing(b, differentPsps ? "nested/a.iso" : "nested/b.iso");
        var consentA = Signal(); var consentB = Signal(); var releaseConsent = Signal();
        var writeA = Signal(); var writeB = Signal(); var releaseWrites = Signal();
        var owner = new CopyDestinationResources();
        var ia = new Interaction { Overwrite = async (_, token) => { consentA.TrySetResult(); await releaseConsent.Task.WaitAsync(token); return CopyOverwriteDecision.Overwrite; } };
        var ib = new Interaction { Overwrite = async (_, token) => { consentB.TrySetResult(); await releaseConsent.Task.WaitAsync(token); return CopyOverwriteDecision.Overwrite; } };
        CopyFileAccessHooks Hooks(TaskCompletionSource entered) => new() { WrapOutput = stream => new ProbeStream(stream,
            async (_, token) => { entered.TrySetResult(); await releaseWrites.Task.WaitAsync(token); }) };
        GameCopyService? sharedEngine = null;
        if (!differentPsps)
        {
            // Reuse the exact same engine, reader and filesystem as production composition.
            var sharedInput = new Interaction { Overwrite = async (prompt, token) => {
                (prompt.DestinationPath.EndsWith("a.iso", StringComparison.Ordinal) ? consentA : consentB).TrySetResult();
                await releaseConsent.Task.WaitAsync(token); return CopyOverwriteDecision.Overwrite;
            } };
            var opened = 0;
            var sharedHooks = new CopyFileAccessHooks { WrapOutput = stream => {
                var entered = Interlocked.Increment(ref opened) == 1 ? writeA : writeB;
                return new ProbeStream(stream, async (_, token) => { entered.TrySetResult(); await releaseWrites.Task.WaitAsync(token); });
            } };
            sharedEngine = Service(source, a, owner, sharedInput, sharedHooks);
        }
        var first = (sharedEngine ?? Service(source, a, owner, ia, Hooks(writeA))).CopyAsync(Request(source, a, "a.zip"));
        var second = (sharedEngine ?? Service(source, b, owner, ib, Hooks(writeB))).CopyAsync(Request(source, b, differentPsps ? "a.zip" : "b.zip"));
        try
        {
            await Task.WhenAll(consentA.Task, consentB.Task).WaitAsync(Deadline);
            releaseConsent.TrySetResult();
            await Task.WhenAll(writeA.Task, writeB.Task).WaitAsync(Deadline);
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        }
        finally { releaseConsent.TrySetResult(); releaseWrites.TrySetResult(); await Task.WhenAll(first, second).WaitAsync(Deadline); }
        AssertCompleted(await first); AssertCompleted(await second);
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(a.Root, "ISO/nested/a.iso")));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(b.Root, differentPsps ? "ISO/nested/a.iso" : "ISO/nested/b.iso")));
        Assert.Equal(sourceHash, Hashes(source.Root)); a.AssertReleased(); b.AssertReleased(); source.AssertReleased();
    }

    private static async Task SameFile(string scenario)
    {
        using var source = new Area(); using var destination = new Area();
        var payload = Payload(); CreateZip(source.Root, "game.zip", "game.iso", payload);
        var sourceHash = Hashes(source.Root); Existing(destination, "game.iso");
        using var stopA = new CancellationTokenSource(); using var stopB = new CancellationTokenSource();
        var consentA = Signal(); var releaseA = Signal(); var waitB = Signal();
        var owner = new CopyDestinationResources();
        var a = new Interaction { Overwrite = async (_, token) => { consentA.TrySetResult(); await releaseA.Task.WaitAsync(token); return CopyOverwriteDecision.Overwrite; } };
        var b = new Interaction();
        b.Overwrite = (prompt, _) => {
            Assert.Equal(scenario == "failed-holder" ? 1 : payload.Length + (scenario == "changed-consent" && b.Overwrites > 1 ? 1 : 0), prompt.Evidence.Length);
            if (scenario == "changed-consent" && b.Overwrites == 1)
                File.WriteAllBytes(Path.Combine(destination.Root, "ISO/game.iso"), [255, .. payload]);
            return ValueTask.FromResult(CopyOverwriteDecision.Overwrite);
        };
        var sa = new Snapshots { OnReport = value => {
            if (scenario == "cancelled-holder" && value.Progress?.Work.WrittenBytes > 0) stopA.Cancel();
        } };
        var sb = new Snapshots { OnReport = value => { if (value.State == GameCopyState.WaitingForResource) waitB.TrySetResult(); } };
        CopyFileAccessHooks? hooks = scenario == "failed-holder" ? new() { WrapOutput = stream => new ProbeStream(stream,
            async (bytes, token) => { await stream.WriteAsync(bytes[..1], token); throw new IOException("Controlled partial write"); }) } : null;
        var first = Service(source, destination, owner, a, hooks).CopyAsync(Request(source, destination, "game.zip"), sa, stopA.Token);
        await consentA.Task.WaitAsync(Deadline);
        var second = Service(source, destination, owner, b).CopyAsync(Request(source, destination, "game.zip"), sb, stopB.Token);
        try
        {
            await waitB.Task.WaitAsync(Deadline);
            Assert.Equal(0, b.Overwrites); Assert.False(second.IsCompleted);
            if (scenario == "cancel-waiter")
            {
                stopB.Cancel(); Assert.Equal(GameCopyState.Cancelled, (await second.WaitAsync(Deadline)).State);
                Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(destination.Root, "ISO/game.iso")));
            }
        }
        finally { releaseA.TrySetResult(); await Task.WhenAll(first, second).WaitAsync(Deadline); }
        if (scenario == "failed-holder") Assert.Equal(GameCopyState.Failed, (await first).State);
        else if (scenario == "cancelled-holder") Assert.Equal(GameCopyState.Cancelled, (await first).State);
        else AssertCompleted(await first);
        if (scenario != "cancel-waiter") { AssertCompleted(await second); Assert.Equal(scenario == "changed-consent" ? 2 : 1, b.Overwrites); }
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(destination.Root, "ISO/game.iso")));
        Assert.Equal(sourceHash, Hashes(source.Root)); destination.AssertReleased(); source.AssertReleased();
        Assert.Null(sb.Values[^1].WaitReason); Assert.Null(sb.Values[^1].Prompt);
    }

    private static async Task Capacity(bool skip)
    {
        using var source = new Area(); using var destination = new Area();
        CreateZip(source.Root, "a.zip", "a.iso", [1, 2, 3], skip);
        CreateZip(source.Root, "b.zip", "b.iso", [4, 5, 6]); Existing(destination, "a.iso");
        var entered = Signal(); var release = Signal(); var owner = new CopyDestinationResources();
        var interaction = new Interaction { Overwrite = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return CopyOverwriteDecision.Skip; } };
        // Initial 53-byte claim fills capacity; omitting 50 bytes admits the second copy.
        var hooks = new CopyFileAccessHooks { AvailableBytes = () => skip ? 53 : 3 };
        var first = Service(source, destination, owner, interaction, hooks).CopyAsync(Request(source, destination, "a.zip"));
        await entered.Task.WaitAsync(Deadline);
        var second = Service(source, destination, owner, new(), hooks).CopyAsync(Request(source, destination, "b.zip"));
        try
        {
            var result = await second.WaitAsync(Deadline);
            if (skip) AssertCompleted(result);
            else { Assert.Equal(GameCopyErrorCode.InsufficientSpace, result.Error!.Code); Assert.False(File.Exists(Path.Combine(destination.Root, "ISO/b.iso"))); }
        }
        finally { release.TrySetResult(); await first.WaitAsync(Deadline); }
        Assert.Equal(GameCopyState.CompletedWithSkippedFiles, (await first).State);
        if (!skip) AssertCompleted(await Service(source, destination, owner, new(), hooks).CopyAsync(Request(source, destination, "b.zip")));
        destination.AssertReleased(); source.AssertReleased();
    }

    private static async Task OwnerFault()
    {
        using var source = new Area(); using var destination = new Area();
        CreateZip(source.Root, "a.zip", "a.iso", Payload()); CreateZip(source.Root, "b.zip", "b.iso", [4, 5, 6]);
        var entered = Signal(); var release = Signal(); var owner = new CopyDestinationResources(); var flushFault = 1;
        var hooks = new CopyFileAccessHooks { WrapOutput = stream => new ProbeStream(stream, flush: () => {
            if (Interlocked.Exchange(ref flushFault, 0) == 1) throw new IOException("Controlled owner flush failure");
        }) };
        var snapshots = new Snapshots { OnReport = value => {
            if (value.Progress?.Work.WrittenBytes > 0 && !entered.Task.IsCompleted)
            { entered.TrySetResult(); release.Task.WaitAsync(Deadline).GetAwaiter().GetResult(); }
        } };
        var interaction = new Interaction();
        var first = Service(source, destination, owner, interaction, hooks).CopyAsync(Request(source, destination, "a.zip"), snapshots);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            var second = await Service(source, destination, owner, new()).CopyAsync(Request(source, destination, "b.zip")).WaitAsync(Deadline);
            AssertCompleted(second); Assert.False(first.IsCompleted);
        }
        finally { release.TrySetResult(); await first.WaitAsync(Deadline); }
        Assert.Equal(GameCopyState.Failed, (await first).State);
        Assert.Contains((await first).Error!.Code, new[] { GameCopyErrorCode.WriteError, GameCopyErrorCode.CloseError });
        Assert.Equal(0, interaction.Passwords);
        Assert.NotEmpty(File.ReadAllBytes(Path.Combine(destination.Root, "ISO/a.iso")));
        destination.AssertReleased(); source.AssertReleased();
    }

    private static async Task PasswordDecision(bool damaged)
    {
        using var source = new Area(); using var destination = new Area();
        const string name = "Rar.encrypted_filesOnly.rar";
        var path = Path.Combine(source.Root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers", name), path);
        if (damaged) { var bytes = File.ReadAllBytes(path); bytes[50000] ^= 0x10; File.WriteAllBytes(path, bytes); }
        var entered = Signal(); var release = Signal(); var owner = new CopyDestinationResources();
        var outputs = new List<Stream>();
        var interaction = new Interaction();
        interaction.Password = async (prompt, token) => {
            if (interaction.Passwords == 1) return new(prompt.Identity, CopyPasswordDecision.Retry, damaged ? "test" : "wrong");
            Assert.Equal(GameCopyErrorCode.IndeterminateEncryptedError, prompt.Reason);
            Assert.All(outputs, stream => Assert.False(stream.CanWrite));
            entered.TrySetResult(); await release.Task.WaitAsync(token);
            return new(prompt.Identity, CopyPasswordDecision.Cancel);
        };
        var first = Service(source, destination, owner, interaction,
            new() { WrapOutput = stream => { outputs.Add(stream); return stream; } }).CopyAsync(Request(source, destination, name));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            var retained = Directory.EnumerateFiles(Path.Combine(destination.Root, "ISO"), "*", SearchOption.AllDirectories).First();
            CreateZip(source.Root, "independent.zip", Path.GetRelativePath(Path.Combine(destination.Root, "ISO"), retained).Replace('\\', '/'), [4, 5, 6]);
            var original = Hashes(source.Root); var otherInput = new Interaction();
            AssertCompleted(await Service(source, destination, owner, otherInput).CopyAsync(Request(source, destination, "independent.zip")).WaitAsync(Deadline));
            Assert.Equal(1, otherInput.Overwrites); Assert.False(first.IsCompleted);
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(retained));
            Assert.Equal(original, Hashes(source.Root));
        }
        finally { release.TrySetResult(); await first.WaitAsync(Deadline); }
        Assert.Equal(GameCopyState.Cancelled, (await first).State);
        destination.AssertReleased(); source.AssertReleased();
    }

    private static async Task RetryCapacity()
    {
        using var source = new Area(); using var destination = new Area();
        const string name = "Rar.encrypted_filesOnly.rar";
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfers", name), Path.Combine(source.Root, name));
        var originals = Hashes(source.Root); long available = 1_000_000;
        string[]? retained = null; int? priorOverwrites = null;
        var interaction = new Interaction();
        interaction.Password = (prompt, _) => {
            if (interaction.Passwords > 1)
            {
                Assert.Equal(GameCopyErrorCode.IndeterminateEncryptedError, prompt.Reason);
                retained = Hashes(Path.Combine(destination.Root, "ISO"));
                priorOverwrites = interaction.Overwrites;
                available = 0;
            }
            return ValueTask.FromResult(new CopyPasswordResponse(prompt.Identity, CopyPasswordDecision.Retry,
                interaction.Passwords == 1 ? "wrong" : "test"));
        };
        var result = await Service(source, destination, new(), interaction, new() { AvailableBytes = () => available })
            .CopyAsync(Request(source, destination, name)).WaitAsync(Deadline);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, result.Error!.Code);
        // An earlier password request may already have left an empty file requiring consent.
        // Insufficient retry capacity must prevent any additional overwrite request/mutation.
        Assert.NotNull(priorOverwrites); Assert.Equal(priorOverwrites.Value, interaction.Overwrites);
        Assert.Equal(2, interaction.Passwords);
        Assert.NotNull(retained); Assert.Equal(retained, Hashes(Path.Combine(destination.Root, "ISO")));
        Assert.Equal(originals, Hashes(source.Root)); destination.AssertReleased(); source.AssertReleased();
    }

    private static GameCopyService Service(Area source, Area destination, CopyDestinationResources owner,
        Interaction interaction, CopyFileAccessHooks? hooks = null) => new(new Routed(source.FileSystem(), destination.FileSystem(hooks)),
            new SevenZipGameArchiveReader(GameArchiveReaderTests.RuntimeRoot), interaction, owner);
    private static GameCopyRequest Request(Area source, Area destination, string name) => new(Guid.NewGuid(),
        new(Guid.NewGuid(), name, GameMedium.Usb, null, "/" + name, GameFormat.Iso, 1, null), source.Evidence, destination.Evidence);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static byte[] Payload() => Enumerable.Range(0, 16384).Select(i => (byte)(i % 251)).ToArray();
    private static void AssertCompleted(GameCopyResult result) => Assert.True(result.State == GameCopyState.Completed, result.Error?.Message);
    private static void Existing(Area area, string path)
    { var full = Path.Combine(area.Root, "ISO", path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, [9]); }
    private static void CreateZip(string root, string name, string entry, byte[] bytes, bool unsafeEntry = false)
    {
        using var zip = ZipFile.Open(Path.Combine(root, name), ZipArchiveMode.Create);
        if (unsafeEntry) { using var unsafeFile = zip.CreateEntry("../unsafe.iso").Open(); unsafeFile.Write(new byte[50]); }
        var parentEnd = entry.LastIndexOf('/');
        if (parentEnd >= 0) zip.CreateEntry(entry[..(parentEnd + 1)]);
        using var output = zip.CreateEntry(entry, CompressionLevel.NoCompression).Open(); output.Write(bytes);
    }
    private static string[] Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order()
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private sealed class Routed(IGameCopyFileSystem source, IGameCopyFileSystem destination) : IGameCopyFileSystem
    {
        public ValueTask<IGameArchiveSource> OpenSourceAsync(CopyVolumeEvidence evidence, string path, CancellationToken token) => source.OpenSourceAsync(evidence, path, token);
        public ValueTask<IGameCopyDestination> OpenDestinationAsync(CopyVolumeEvidence evidence, CancellationToken token) => destination.OpenDestinationAsync(evidence, token);
    }
    private sealed class Interaction : IGameCopyInteraction
    {
        internal int Overwrites, Passwords;
        internal Func<CopyOverwritePrompt, CancellationToken, ValueTask<CopyOverwriteDecision>>? Overwrite;
        internal Func<CopyPasswordPrompt, CancellationToken, ValueTask<CopyPasswordResponse>>? Password;
        public async ValueTask<CopyOverwriteResponse> ConfirmOverwriteAsync(CopyOverwritePrompt prompt, CancellationToken token)
        { Interlocked.Increment(ref Overwrites); return new(prompt.Identity, Overwrite is null ? CopyOverwriteDecision.Overwrite : await Overwrite(prompt, token)); }
        public ValueTask<CopyPasswordResponse> RequestPasswordAsync(CopyPasswordPrompt prompt, CancellationToken token)
        { Interlocked.Increment(ref Passwords); return Password?.Invoke(prompt, token) ?? ValueTask.FromResult(new CopyPasswordResponse(prompt.Identity, CopyPasswordDecision.Cancel)); }
    }
    private sealed class Snapshots : IProgress<GameCopySnapshot>
    {
        internal readonly List<GameCopySnapshot> Values = [];
        internal Action<GameCopySnapshot>? OnReport;
        public void Report(GameCopySnapshot value) { Values.Add(value); OnReport?.Invoke(value); }
    }
    private sealed class Area : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "pspman-t15-" + Guid.NewGuid().ToString("N"));
        internal readonly CopyVolumeEvidence Evidence;
        internal Area()
        {
            Directory.CreateDirectory(Root);
            var key = new DeviceKey("t15", Guid.NewGuid().ToString("N")); var identity = new DeviceIdentity(key.DeviceId, DeviceIdentityConfidence.Persistent);
            Evidence = new(key, identity, "g1", new(key, OperatingSystem.IsWindows() ? WindowsCopyNative.ResolveMount(Root).VolumePath : "volume"),
                identity, Root, OperatingSystem.IsWindows() ? MountedPathSyntax.Windows : MountedPathSyntax.Linux);
        }
        internal FileGameCopyFileSystem FileSystem(CopyFileAccessHooks? hooks = null) => new(_ => ValueTask.FromResult(new DeviceInventorySnapshot(1,
            [new(Evidence.Device, "Fixture", DeviceMedium.Psp, DeviceCapabilities.PspDestination | DeviceCapabilities.RemovableSource,
                Evidence.DeviceIdentity, "g1", DeviceState.Ready, [new(Evidence.Volume, null, Root, Evidence.VolumeIdentity, DeviceState.Ready)])])), hooks ?? new());
        internal void AssertReleased()
        {
            if (OperatingSystem.IsLinux()) Assert.Equal(0, LinuxCopySourceTests.OpenDescriptorsBelow(Root));
            Directory.Move(Root, Root + "-released"); Directory.Move(Root + "-released", Root);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class ProbeStream(Stream inner, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? beforeWrite = null,
        Action? flush = null) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        { if (beforeWrite is not null) await beforeWrite(bytes, token); await inner.WriteAsync(bytes, token); }
        public override void Write(byte[] bytes, int offset, int count) => WriteAsync(bytes.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { flush?.Invoke(); inner.Flush(); }
        public override async Task FlushAsync(CancellationToken token) { flush?.Invoke(); await inner.FlushAsync(token); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long length) => inner.SetLength(length);
    }
}
