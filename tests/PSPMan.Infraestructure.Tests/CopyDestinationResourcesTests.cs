using PSPMan.Core.Transfers;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

public sealed class CopyDestinationResourcesTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [LinuxCopyFact]
    public async Task Same_target_waits_cancellably_but_other_files_continue_and_reinspect_retained_bytes()
    {
        using var tree = new CopyTestTree();
        var resources = new CopyDestinationResources();
        await using var rawA = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var rawB = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, rawA);
        var b = resources.BindDestination(bId, tree.Evidence, rawB);
        await using var aBudget = await resources.ReserveAsync(aId, tree.Evidence, a, 20, default);
        await using var bBudget = await resources.ReserveAsync(bId, tree.Evidence, b, 20, default);
        var entry = await resources.AcquireEntryAsync(aId, tree.Evidence, a, ["game.iso"], false, null, default);
        Assert.Equal(CopyTargetKind.Missing, (await a.InspectAsync(["game.iso"], default)).Kind);
        var file = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1, 2, 3 });
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var wait = resources.AcquireEntryAsync(bId, tree.Evidence, b, ["game.iso"], false,
            new InlineProgress(_ => waiting.TrySetResult()), cancel.Token).AsTask();
        await waiting.Task.WaitAsync(Deadline); Assert.False(wait.IsCompleted);
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        await using (var other = await resources.AcquireEntryAsync(bId, tree.Evidence, b, ["other.iso"], false, null, default))
        await using (var output = (await b.OpenOutputAsync(["other.iso"], null, default)).File!)
            await output.Output.WriteAsync(new byte[] { 9 });
        waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wait = resources.AcquireEntryAsync(bId, tree.Evidence, b, ["game.iso"], false,
            new InlineProgress(_ => waiting.TrySetResult()), default).AsTask();
        await waiting.Task.WaitAsync(Deadline);
        await entry.DisposeAsync(); // Closes the owned file before waking the next holder.
        await using var next = await wait.WaitAsync(Deadline);
        var snapshot = await b.InspectAsync(["game.iso"], default);
        Assert.Equal(3, snapshot.Evidence!.Length);
        Assert.True((await b.OpenOutputAsync(["game.iso"], null, default)).TargetChanged);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
        await file.DisposeAsync();
    }

    [LinuxCopyFact]
    public async Task Reservations_replace_retry_claims_and_reject_unknown_or_overcommitted_space_without_mutation()
    {
        using var tree = new CopyTestTree(); long? available = 100;
        var fs = tree.CreateFileSystem(new() { AvailableBytes = () => available });
        await using var raw = await fs.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, raw);
        var b = resources.BindDestination(bId, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(aId, tree.Evidence, a, 70, default);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await resources.ReserveAsync(bId, tree.Evidence, b, 31, default))).Error.Code);
        await budget.RevalidateAsync(70, default);
        await budget.SkipPendingAsync(40, default);
        await using var other = await resources.ReserveAsync(bId, tree.Evidence, b, 70, default);
        available = null;
        Assert.Equal(GameCopyErrorCode.UnknownFreeSpace, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await budget.RevalidateAsync(30, default))).Error.Code);
        available = 100;
        Assert.False(Directory.Exists(Path.Combine(tree.Root, "ISO")));
    }

    private sealed class InlineProgress(Action<GameCopyResourceWait> action) : IProgress<GameCopyResourceWait>
    { public void Report(GameCopyResourceWait value) => action(value); }

    [LinuxCopyTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_fence_drains_writes_before_sampling_and_does_not_double_charge(bool allocateBeforePause)
    {
        using var tree = new CopyTestTree(); long allocated = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hooks = new CopyFileAccessHooks { AvailableBytes = () => 100 - Interlocked.Read(ref allocated),
            WrapOutput = stream => new ProbeStream(stream, async (bytes, token) =>
            {
                if (allocateBeforePause) Interlocked.Add(ref allocated, bytes.Length);
                entered.TrySetResult(); await release.Task.WaitAsync(token);
                if (!allocateBeforePause) Interlocked.Add(ref allocated, bytes.Length);
            }) };
        await using var raw = await tree.CreateFileSystem(hooks).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, raw); var b = resources.BindDestination(bId, tree.Evidence, raw);
        await using var aBudget = await resources.ReserveAsync(aId, tree.Evidence, a, 70, default);
        await using var entry = await resources.AcquireEntryAsync(aId, tree.Evidence, a, ["game.iso"], false, null, default);
        await using var file = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        var write = file.Output.WriteAsync(new byte[30]).AsTask();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            var admission = resources.ReserveAsync(bId, tree.Evidence, b, 30, default).AsTask();
            Assert.False(admission.IsCompleted);
            release.TrySetResult(); await write.WaitAsync(Deadline);
            await using var bBudget = await admission.WaitAsync(Deadline);
            Assert.Equal(30, new FileInfo(Path.Combine(tree.Root, "ISO", "game.iso")).Length);
            // A pending 40 + B pending 30 exactly fits the remaining 70.
            await Assert.ThrowsAsync<GameCopyException>(async () => await bBudget.RevalidateAsync(31, default));
        }
        finally { release.TrySetResult(); await write; }
    }

    [LinuxCopyFact]
    public async Task Different_native_files_write_simultaneously_without_a_volume_transfer_lock()
    {
        using var tree = new CopyTestTree();
        var enteredA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CopyFileAccessHooks Hooks(TaskCompletionSource entered) => new() { WrapOutput = stream =>
            new ProbeStream(stream, async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); }) };
        await using var rawA = await tree.CreateFileSystem(Hooks(enteredA)).OpenDestinationAsync(tree.Evidence, default);
        await using var rawB = await tree.CreateFileSystem(Hooks(enteredB)).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, rawA); var b = resources.BindDestination(bId, tree.Evidence, rawB);
        await using var ab = await resources.ReserveAsync(aId, tree.Evidence, a, 3, default);
        await using var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 4, default);
        await using var ae = await resources.AcquireEntryAsync(aId, tree.Evidence, a, ["same", "a.iso"], false, null, default);
        await using var be = await resources.AcquireEntryAsync(bId, tree.Evidence, b, ["same", "b.iso"], false, null, default);
        await using var af = (await a.OpenOutputAsync(["same", "a.iso"], null, default)).File!;
        await using var bf = (await b.OpenOutputAsync(["same", "b.iso"], null, default)).File!;
        var first = af.Output.WriteAsync(new byte[] { 1, 2, 3 }).AsTask();
        var second = bf.Output.WriteAsync(new byte[] { 4, 5, 6, 7 }).AsTask();
        try { await Task.WhenAll(enteredA.Task, enteredB.Task).WaitAsync(Deadline); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); }
        finally { release.TrySetResult(); await Task.WhenAll(first, second); }
        await ae.DisposeAsync(); await be.DisposeAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "same", "a.iso")));
        Assert.Equal(new byte[] { 4, 5, 6, 7 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "same", "b.iso")));
    }

    [LinuxCopyFact]
    public async Task Buffered_native_output_is_flushed_before_the_next_capacity_admission()
    {
        using var tree = new CopyTestTree();
        string path = Path.Combine(tree.Root, "ISO", "game.iso");
        var hooks = new CopyFileAccessHooks { WrapOutput = s => new BufferedStream(s, 8192),
            AvailableBytes = () => 100 - (File.Exists(path) ? new FileInfo(path).Length : 0) };
        await using var raw = await tree.CreateFileSystem(hooks).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid();
        var a = resources.BindDestination(id, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(id, tree.Evidence, a, 30, default);
        await using var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default);
        await using var file = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[30]);
        Assert.Equal(0, new FileInfo(path).Length);
        await budget.RevalidateAsync(70, default);
        Assert.Equal(30, new FileInfo(path).Length);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await budget.RevalidateAsync(71, default))).Error.Code);
    }

    [LinuxCopyFact]
    public async Task Closing_a_budget_cancels_its_pending_entry_wait_and_preserves_the_holder()
    {
        using var tree = new CopyTestTree();
        await using var raw = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, raw); var b = resources.BindDestination(bId, tree.Evidence, raw);
        await using var ab = await resources.ReserveAsync(aId, tree.Evidence, a, 1, default);
        var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 1, default);
        await using var ae = await resources.AcquireEntryAsync(aId, tree.Evidence, a, ["game.iso"], false, null, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = resources.AcquireEntryAsync(bId, tree.Evidence, b, ["game.iso"], false, new InlineProgress(_ => entered.TrySetResult()), default).AsTask();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            await bb.DisposeAsync().AsTask().WaitAsync(Deadline);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));
            Assert.Equal(CopyTargetKind.Missing, (await a.InspectAsync(["game.iso"], default)).Kind);
        }
        finally { await ae.DisposeAsync(); try { await (await pending.WaitAsync(Deadline)).DisposeAsync(); } catch (OperationCanceledException) { } }
    }

    [LinuxCopyTheory]
    [InlineData("partial")]
    [InlineData("uncertain")]
    [InlineData("close")]
    public async Task Faults_retain_native_bytes_and_release_conflicts_without_refunding_output(string mode)
    {
        using var tree = new CopyTestTree(); var path = Path.Combine(tree.Root, "ISO", "game.iso");
        var hooks = new CopyFileAccessHooks { AvailableBytes = () => 100 - (File.Exists(path) ? new FileInfo(path).Length : 0),
            WrapOutput = s => new FaultStream(s, mode) };
        await using var raw = await tree.CreateFileSystem(hooks).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid();
        var a = resources.BindDestination(id, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(id, tree.Evidence, a, 10, default);
        var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default);
        var output = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        if (mode == "close") await output.Output.WriteAsync(new byte[] { 1, 2, 3, 4 });
        else Assert.Equal(GameCopyErrorCode.WriteError, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await output.Output.WriteAsync(new byte[] { 1, 2, 3, 4 }))).Error.Code);
        if (mode == "close") Assert.Equal(GameCopyErrorCode.CloseError,
            (await Assert.ThrowsAsync<GameCopyException>(async () => await entry.DisposeAsync())).Error.Code);
        else await entry.DisposeAsync();
        var bytes = File.ReadAllBytes(path); Assert.Equal(mode == "close" ? 4 : 2, bytes.Length);
        await budget.RevalidateAsync(100 - bytes.Length, default);
        await Assert.ThrowsAsync<GameCopyException>(async () => await budget.RevalidateAsync(101 - bytes.Length, default));
        await using var again = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default);
        Assert.Equal(bytes.Length, (await a.InspectAsync(["game.iso"], default)).Evidence!.Length);
        Assert.True((await a.OpenOutputAsync(["game.iso"], null, default)).TargetChanged);
    }

    [LinuxCopyFact]
    public async Task Root_replacement_never_redirects_a_claim_or_a_write()
    {
        using var tree = new CopyTestTree();
        await using var raw = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid();
        var a = resources.BindDestination(id, tree.Evidence, raw);
        var budget = await resources.ReserveAsync(id, tree.Evidence, a, 10, default);
        var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default);
        Directory.Move(tree.Root, tree.Root + "-old"); Directory.CreateDirectory(tree.Root);
        Assert.Equal(GameCopyErrorCode.DestinationChanged, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await a.OpenOutputAsync(["game.iso"], null, default))).Error.Code);
        await entry.DisposeAsync();
        await Assert.ThrowsAsync<GameCopyException>(async () => await budget.DisposeAsync());
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyFact]
    public async Task Changed_target_requires_fresh_consent_and_case_equality_comes_from_native_lookup()
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        string path = Path.Combine(tree.Root, "ISO", "game.iso"); File.WriteAllText(path, "old");
        await using var raw = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid();
        var a = resources.BindDestination(id, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(id, tree.Evidence, a, 10, default);
        await using (var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default))
        {
            var old = (await a.InspectAsync(["game.iso"], default)).Evidence;
            File.Move(path, path + ".old"); File.WriteAllText(path, "replacement");
            Assert.True((await a.OpenOutputAsync(["game.iso"], old, default)).TargetChanged);
            var current = (await a.InspectAsync(["game.iso"], default)).Evidence;
            await using var output = (await a.OpenOutputAsync(["game.iso"], current, default)).File!;
            await output.Output.WriteAsync(new byte[] { 7 });
        }
        await using var upper = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["UPPER.iso"], false, null, default);
        var secondId = Guid.NewGuid(); var b = resources.BindDestination(secondId, tree.Evidence, raw);
        await using var bb = await resources.ReserveAsync(secondId, tree.Evidence, b, 1, default);
        using var cancel = new CancellationTokenSource(Deadline);
        var lower = resources.AcquireEntryAsync(secondId, tree.Evidence, b, ["upper.iso"], false, null, cancel.Token).AsTask();
        if (File.Exists(Path.Combine(tree.Root, "ISO", "upper.iso")) && !lower.IsCompleted)
        { cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lower); }
        else { await using var le = await lower; Assert.NotEqual(upper.TargetIdentity, le.TargetIdentity); }
        Assert.Equal("old", File.ReadAllText(path + ".old")); Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(path));
    }

    [LinuxCopyTheory]
    [InlineData("directory")]
    [InlineData("structure")]
    [InlineData("unsafe")]
    [InlineData("link")]
    [InlineData("name")]
    public async Task Shared_namespace_preserves_directory_structural_name_and_skip_outcomes(string mode)
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO"));
        if (mode == "structure") File.WriteAllText(Path.Combine(tree.Root, "ISO", "parent"), "keep");
        if (mode == "link") Directory.CreateSymbolicLink(Path.Combine(tree.Root, "ISO", "parent"), tree.Outside);
        await using var rawA = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var rawB = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, rawA); var b = resources.BindDestination(bId, tree.Evidence, rawB);
        await using var ab = await resources.ReserveAsync(aId, tree.Evidence, a, 1, default);
        await using var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 1, default);
        System.Collections.Immutable.ImmutableArray<string> path = mode == "unsafe" ? ["..", "escape"] : mode == "name" ? ["bad\0name"] : ["parent", "child"];
        if (mode is "structure" or "name")
        {
            var error = await Assert.ThrowsAsync<GameCopyException>(async () =>
                await resources.AcquireEntryAsync(aId, tree.Evidence, a, path, true, null, default));
            Assert.Equal(mode == "name" ? GameCopyErrorCode.InvalidName : GameCopyErrorCode.FileDirectoryConflict, error.Error.Code);
            if (mode == "structure") Assert.Equal("keep", File.ReadAllText(Path.Combine(tree.Root, "ISO", "parent")));
        }
        else
        {
            var ae = await resources.AcquireEntryAsync(aId, tree.Evidence, a, path, true, null, default);
            var skipped = await a.EnsureDirectoryAsync(path, default);
            Assert.Equal(mode == "unsafe" ? GameCopySkipReason.UnsafePath : mode == "link" ? GameCopySkipReason.Link : null, skipped);
            await ae.DisposeAsync();
            await using var be = await resources.AcquireEntryAsync(bId, tree.Evidence, b, path, true, null, default);
            Assert.Equal(skipped, await b.EnsureDirectoryAsync(path, default));
        }
        Assert.Empty(Directory.GetFileSystemEntries(tree.Outside));
    }

    [LinuxCopyFact]
    public async Task Two_operations_merge_new_directories_without_claiming_the_whole_tree()
    {
        using var tree = new CopyTestTree();
        await using var rawA = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var rawB = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, rawA); var b = resources.BindDestination(bId, tree.Evidence, rawB);
        await using var ab = await resources.ReserveAsync(aId, tree.Evidence, a, 1, default);
        await using var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 1, default);
        var first = resources.AcquireEntryAsync(aId, tree.Evidence, a, ["new", "a.iso"], false, null, default).AsTask();
        var second = resources.AcquireEntryAsync(bId, tree.Evidence, b, ["new", "b.iso"], false, null, default).AsTask();
        await Task.WhenAll(first, second).WaitAsync(Deadline);
        await using var ae = await first; await using var be = await second;
        Assert.NotEqual(ae.TargetIdentity, be.TargetIdentity);
        Assert.True(Directory.Exists(Path.Combine(tree.Root, "ISO", "new")));
    }

    [LinuxCopyFact]
    public async Task Manual_epochs_keep_uncertain_charges_until_sampling_and_cancelled_fences_do_not_steal_capacity()
    {
        using var tree = new CopyTestTree(); long available = 100;
        await using var raw = await tree.CreateFileSystem(new() { AvailableBytes = () => Interlocked.Read(ref available) }).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, raw); var b = resources.BindDestination(bId, tree.Evidence, raw);
        await using var ab = await resources.ReserveAsync(aId, tree.Evidence, a, 70, default);
        var write = await ab.BeginWriteAsync(30, default);
        using var cancel = new CancellationTokenSource();
        var pending = resources.ReserveAsync(bId, tree.Evidence, b, 30, cancel.Token).AsTask();
        Assert.False(pending.IsCompleted); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await write.DisposeAsync(); // Unknown accepted size is conservatively the full request.
        available = 70;
        await using var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 30, default);
        await Assert.ThrowsAsync<GameCopyException>(async () => await bb.RevalidateAsync(31, default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await write.CompleteAsync(1));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ab.BeginWriteAsync(1, cancelled.Token));
        await ab.SkipPendingAsync(40, default);
        await bb.RevalidateAsync(70, default);
    }

    [LinuxCopyTheory]
    [InlineData(-1, GameCopyErrorCode.InvalidActualSize)]
    [InlineData(101, GameCopyErrorCode.InsufficientSpace)]
    public async Task Invalid_or_excess_actual_sizes_do_not_create_destination_entries(long pending, GameCopyErrorCode expected)
    {
        using var tree = new CopyTestTree();
        await using var raw = await tree.CreateFileSystem(new() { AvailableBytes = () => 100 }).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        Assert.Equal(expected, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await resources.ReserveAsync(id, tree.Evidence, a, pending, default))).Error.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default));
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyFact]
    public async Task Failed_capacity_sample_preserves_budget_then_allows_a_fresh_consistent_sample()
    {
        using var tree = new CopyTestTree(); bool fail = false;
        await using var raw = await tree.CreateFileSystem(new() { AvailableBytes = () => fail ? throw new IOException("sample failure") : 100 }).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(id, tree.Evidence, a, 70, default);
        await using (var write = await budget.BeginWriteAsync(20, default)) await write.CompleteAsync(20);
        fail = true;
        Assert.Equal(GameCopyErrorCode.UnknownFreeSpace, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await budget.RevalidateAsync(70, default))).Error.Code);
        fail = false; await budget.RevalidateAsync(70, default);
        await budget.SkipPendingAsync(70, default);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await budget.BeginWriteAsync(1, default));
    }

    [LinuxCopyFact]
    public async Task Closing_during_a_cooperative_write_drains_the_chunk_and_releases_native_handles()
    {
        using var tree = new CopyTestTree();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var baseline = LinuxCopySourceTests.OpenDescriptorsBelow(tree.Root);
        var fs = tree.CreateFileSystem(new() { WrapOutput = s => new ProbeStream(s, async (_, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }) });
        var raw = await fs.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        var budget = await resources.ReserveAsync(id, tree.Evidence, a, 10, default);
        await using var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default);
        await using var file = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        var write = file.Output.WriteAsync(new byte[] { 1, 2, 3 }).AsTask();
        await entered.Task.WaitAsync(Deadline);
        await budget.DisposeAsync().AsTask().WaitAsync(Deadline);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        await raw.DisposeAsync();
        Assert.Equal(baseline, LinuxCopySourceTests.OpenDescriptorsBelow(tree.Root));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task Failed_terminal_sample_stays_conservative_and_is_reconciled_by_a_later_admission()
    {
        using var tree = new CopyTestTree(); long? available = 100;
        await using var raw = await tree.CreateFileSystem(new() { AvailableBytes = () => available }).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        var budget = await resources.ReserveAsync(id, tree.Evidence, a, 70, default);
        await using (var epoch = await budget.BeginWriteAsync(20, default)) await epoch.CompleteAsync(20);
        available = null;
        await Assert.ThrowsAsync<GameCopyException>(async () => await budget.DisposeAsync());
        var bId = Guid.NewGuid(); var b = resources.BindDestination(bId, tree.Evidence, raw);
        await Assert.ThrowsAsync<GameCopyException>(async () => await resources.ReserveAsync(bId, tree.Evidence, b, 80, default));
        available = 80;
        await using var next = await resources.ReserveAsync(bId, tree.Evidence, b, 80, default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await budget.BeginWriteAsync(1, default));
    }

    [LinuxCopyFact]
    public async Task Uncertain_epoch_closure_waits_for_completion_instead_of_refunding_in_flight_bytes()
    {
        using var tree = new CopyTestTree();
        await using var raw = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        var budget = await resources.ReserveAsync(id, tree.Evidence, a, 20, default);
        var epoch = await budget.BeginWriteAsync(20, default);
        var closing = budget.DisposeAsync().AsTask();
        try { Assert.False(closing.IsCompleted); }
        finally { await epoch.DisposeAsync(); await closing.WaitAsync(Deadline); }
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyTheory]
    [InlineData("claim")]
    [InlineData("directory")]
    public async Task Late_parent_links_are_skipped_at_native_creation_without_outside_writes(string stage)
    {
        using var tree = new CopyTestTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "ISO")); bool swapped = false;
        var fs = tree.CreateFileSystem(new() { BeforeMutation = (point, relative) =>
        {
            if (point != stage || swapped || stage == "directory" && relative != "ISO/nested") return;
            swapped = true;
            Directory.Move(Path.Combine(tree.Root, "ISO"), Path.Combine(tree.Root, "retired"));
            Directory.CreateSymbolicLink(Path.Combine(tree.Root, "ISO"), tree.Outside);
        }});
        await using var raw = await fs.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(id, tree.Evidence, a, 1, default);
        await using var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["nested", "game.iso"], false, null, default);
        Assert.True(swapped);
        Assert.Equal(GameCopySkipReason.Link, (await a.OpenOutputAsync(["nested", "game.iso"], null, default)).SkipReason);
        Assert.Empty(Directory.GetFileSystemEntries(tree.Outside));
    }

    [LinuxCopyFact]
    public async Task Independent_volumes_have_separate_budgets_and_duplicate_operation_ids_are_rejected()
    {
        using var left = new CopyTestTree(); using var right = new CopyTestTree();
        var otherDevice = right.Evidence.Device with { DeviceId = "second-device" };
        var evidence = right.Evidence with { Device = otherDevice, Volume = right.Evidence.Volume with { Device = otherDevice } };
        var inventory = right.Inventory();
        inventory = inventory with { Devices = [inventory.Devices[0] with { Key = otherDevice,
            Volumes = [inventory.Devices[0].Volumes[0] with { Key = evidence.Volume }] }] };
        var fsRight = new FileGameCopyFileSystem(_ => ValueTask.FromResult(inventory), new() { AvailableBytes = () => 100 });
        await using var rawA = await left.CreateFileSystem(new() { AvailableBytes = () => 100 }).OpenDestinationAsync(left.Evidence, default);
        await using var rawB = await fsRight.OpenDestinationAsync(evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, left.Evidence, rawA); var b = resources.BindDestination(bId, evidence, rawB);
        await using var ab = await resources.ReserveAsync(aId, left.Evidence, a, 100, default);
        var duplicate = resources.BindDestination(aId, evidence, rawB);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await resources.ReserveAsync(aId, evidence, duplicate, 1, default));
        await using var bb = await resources.ReserveAsync(bId, evidence, b, 100, default);
        Assert.NotEqual(ab.Volume, bb.Volume);
    }

    [LinuxCopyFact]
    public async Task A_peer_flush_failure_is_reported_by_its_owner_without_failing_an_unrelated_admission()
    {
        using var tree = new CopyTestTree(); string path = Path.Combine(tree.Root, "ISO", "game.iso");
        await using var rawA = await tree.CreateFileSystem(new() { WrapOutput = s => new FaultStream(s, "flush"),
            AvailableBytes = () => 100 - (File.Exists(path) ? new FileInfo(path).Length : 0) }).OpenDestinationAsync(tree.Evidence, default);
        await using var rawB = await tree.CreateFileSystem(new() {
            AvailableBytes = () => 100 - (File.Exists(path) ? new FileInfo(path).Length : 0) }).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, rawA); var b = resources.BindDestination(bId, tree.Evidence, rawB);
        var budget = await resources.ReserveAsync(aId, tree.Evidence, a, 2, default);
        var entry = await resources.AcquireEntryAsync(aId, tree.Evidence, a, ["game.iso"], false, null, default);
        var output = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        await output.Output.WriteAsync(new byte[] { 1, 2 });
        try
        {
            await using var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 98, default);
            await Assert.ThrowsAsync<GameCopyException>(async () => await budget.DisposeAsync());
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(path));
            await entry.DisposeAsync(); await output.DisposeAsync();
        }
        finally { try { await budget.DisposeAsync(); } catch (GameCopyException) { } }
    }

    [LinuxCopyFact]
    public async Task A_second_open_cannot_truncate_an_output_still_owned_by_the_same_entry()
    {
        using var tree = new CopyTestTree();
        await using var raw = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var id = Guid.NewGuid(); var a = resources.BindDestination(id, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(id, tree.Evidence, a, 10, default);
        await using var entry = await resources.AcquireEntryAsync(id, tree.Evidence, a, ["game.iso"], false, null, default);
        await using var file = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1, 2 });
        var consent = (await a.InspectAsync(["game.iso"], default)).Evidence;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await a.OpenOutputAsync(["game.iso"], consent, default));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(tree.Root, "ISO", "game.iso")));
    }

    [LinuxCopyFact]
    public async Task Aggregate_overflow_is_a_reported_size_error_without_mutation()
    {
        using var tree = new CopyTestTree();
        await using var raw = await tree.CreateFileSystem(new() { AvailableBytes = () => long.MaxValue }).OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, raw); var b = resources.BindDestination(bId, tree.Evidence, raw);
        await using var budget = await resources.ReserveAsync(aId, tree.Evidence, a, long.MaxValue, default);
        Assert.Equal(GameCopyErrorCode.SizeOverflow, (await Assert.ThrowsAsync<GameCopyException>(async () =>
            await resources.ReserveAsync(bId, tree.Evidence, b, 1, default))).Error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(tree.Root));
    }

    [LinuxCopyFact]
    public async Task Renaming_an_active_file_does_not_release_its_inode_before_output_close()
    {
        using var tree = new CopyTestTree();
        await using var rawA = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        await using var rawB = await tree.FileSystem.OpenDestinationAsync(tree.Evidence, default);
        var resources = new CopyDestinationResources(); var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
        var a = resources.BindDestination(aId, tree.Evidence, rawA); var b = resources.BindDestination(bId, tree.Evidence, rawB);
        await using var ab = await resources.ReserveAsync(aId, tree.Evidence, a, 10, default);
        await using var bb = await resources.ReserveAsync(bId, tree.Evidence, b, 10, default);
        var ae = await resources.AcquireEntryAsync(aId, tree.Evidence, a, ["game.iso"], false, null, default);
        await using var file = (await a.OpenOutputAsync(["game.iso"], null, default)).File!;
        await file.Output.WriteAsync(new byte[] { 1, 2 });
        string path = Path.Combine(tree.Root, "ISO", "game.iso"); File.Move(path, path + ".moved"); File.WriteAllText(path, "replacement");
        _ = await a.InspectAsync(["game.iso"], default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = resources.AcquireEntryAsync(bId, tree.Evidence, b, ["game.iso.moved"], false,
            new InlineProgress(_ => entered.TrySetResult()), default).AsTask();
        try
        {
            await entered.Task.WaitAsync(Deadline); Assert.False(waiter.IsCompleted);
            Assert.Equal(GameCopyErrorCode.DestinationChanged, (await Assert.ThrowsAsync<GameCopyException>(async () =>
                await file.Output.WriteAsync(new byte[] { 3 }))).Error.Code);
        }
        finally { await ae.DisposeAsync(); }
        await using var be = await waiter.WaitAsync(Deadline);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(path + ".moved"));
        Assert.Equal("replacement", File.ReadAllText(path));
    }

    private sealed class ProbeStream(Stream inner, Func<ReadOnlyMemory<byte>, CancellationToken, Task> pause) : Stream
    {
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => inner.CanSeek;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        { await inner.WriteAsync(bytes, token); await pause(bytes, token); }
        public override void Write(byte[] b, int o, int c) => WriteAsync(b.AsMemory(o, c)).AsTask().GetAwaiter().GetResult();
        public override void Flush() => inner.Flush(); public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => inner.Seek(o, s); public override void SetLength(long v) => inner.SetLength(v);
    }
    private sealed class FaultStream(Stream inner, string mode) : Stream
    {
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => mode != "uncertain";
        public override long Length => inner.Length; public override long Position { get => mode == "uncertain" ? throw new NotSupportedException() : inner.Position; set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        { await inner.WriteAsync(mode is "close" or "flush" ? bytes : bytes[..2], token); if (mode is not ("close" or "flush")) throw new IOException("T13 partial write."); }
        public override void Write(byte[] b, int o, int c) => WriteAsync(b.AsMemory(o, c)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { if (mode == "flush") throw new IOException("T13 flush failure."); inner.Flush(); }
        public override Task FlushAsync(CancellationToken token) => mode == "flush" ? Task.FromException(new IOException("T13 flush failure.")) : inner.FlushAsync(token);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); if (mode == "close") throw new IOException("T13 close failure."); } base.Dispose(disposing); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }
}
