using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;

namespace PSPMan.Core.Tests;

public sealed class GameCopyConcurrencyRulesTests
{
    private static readonly DeviceKey Source = new("test", "source"), OtherSource = new("test", "other-source"),
        Psp = new("test", "psp"), OtherPsp = new("test", "other-psp");
    private static GameRecord Game => new(Guid.NewGuid(), "Game", GameMedium.Usb, null, "/nested/game.zip", GameFormat.Iso, 10, null);
    private static DeviceSnapshot Device(DeviceKey key, bool psp, string root, bool optical = false) => new(key, "Same label",
        psp ? DeviceMedium.Psp : optical ? DeviceMedium.Optical : DeviceMedium.Usb,
        psp ? DeviceCapabilities.PspDestination : optical ? DeviceCapabilities.OpticalSource : DeviceCapabilities.RemovableSource,
        new(key.DeviceId, DeviceIdentityConfidence.Persistent), "generation", DeviceState.Ready,
        [new(new(key, "volume"), "Same label", root, new(key.DeviceId + "-volume", DeviceIdentityConfidence.Persistent), DeviceState.Ready)]);
    private static DeviceInventorySnapshot Inventory => new(1, [Device(Source, false, "/source"), Device(Psp, true, "/psp")]);
    private static CopyVolumeEvidence Volume(DeviceKey? psp = null, string generation = "generation", string root = "/psp")
    {
        var d = Device(psp ?? Psp, true, root);
        return new(d.Key, d.Identity, generation, d.Volumes[0].Key, d.Volumes[0].Identity, root, MountedPathSyntax.Linux);
    }

    [Fact]
    public void Batch_captures_ready_games_and_reports_each_incident_without_mutating_preferences()
    {
        var good = Game; var missing = Game with { Location = "/other.zip" };
        var state = new DeviceSessionState();
        var batch = GameCopySubmissionRules.Capture([
            new(Guid.NewGuid(), good), new(Guid.NewGuid(), missing, SourceDevice: OtherSource)], Inventory, state, MountedPathSyntax.Linux);
        var request = Assert.Single(batch.Accepted);
        Assert.Equal(good, request.Game); Assert.Equal(Source, request.Source.Device); Assert.Equal(Psp, request.Destination.Device);
        var incident = Assert.Single(batch.Incidents);
        Assert.Equal(1, incident.SubmissionIndex); Assert.Equal(missing.Id, incident.GameId);
        Assert.Equal(GameCopyErrorCode.SourceUnavailable, incident.Error.Code);
        Assert.Null(state.RemovableSource); Assert.Empty(state.PspDestinations);
    }

    [Fact]
    public void Optical_choices_are_local_and_the_same_game_can_have_two_independent_destinations()
    {
        var game = Game with { Medium = GameMedium.Dvd, DvdNumber = 1 };
        var inventory = new DeviceInventorySnapshot(4, [Device(Source, false, "/dvd1", true), Device(OtherSource, false, "/dvd2", true),
            Device(Psp, true, "/psp1"), Device(OtherPsp, true, "/psp2")]);
        var preferences = DeviceAssociationRules.ChooseSource(new(), SourceGroup.Optical, Source);
        var batch = GameCopySubmissionRules.Capture([
            new(Guid.NewGuid(), game, Source, null, Psp), new(Guid.NewGuid(), game, OtherSource, null, OtherPsp)],
            inventory, preferences, MountedPathSyntax.Linux);
        Assert.Empty(batch.Incidents); Assert.Equal(2, batch.Accepted.Length);
        Assert.Equal(new[] { Source, OtherSource }, batch.Accepted.Select(r => r.Source.Device));
        Assert.Equal(new[] { Psp, OtherPsp }, batch.Accepted.Select(r => r.Destination.Device));
        Assert.NotEqual(batch.Accepted[0].OperationId, batch.Accepted[1].OperationId);
        Assert.Equal(Source, preferences.OpticalSource);
        var changed = game with { Location = "/changed.zip" };
        inventory = inventory with { Devices = [] };
        Assert.Equal("/nested/game.zip", batch.Accepted[0].Game.Location);
        Assert.NotEqual(changed, batch.Accepted[0].Game);
        Assert.Equal("/dvd2", batch.Accepted[1].Source.Root);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("psp")]
    [InlineData("volume")]
    [InlineData("invalid-path")]
    [InlineData("unavailable-preference")]
    public void Unresolved_or_unavailable_choices_never_fall_back(string scenario)
    {
        var inventory = Inventory; var state = new DeviceSessionState(); var game = Game;
        if (scenario == "source") inventory = inventory with { Devices = inventory.Devices.Add(Device(OtherSource, false, "/other")) };
        if (scenario == "psp") inventory = inventory with { Devices = inventory.Devices.Add(Device(OtherPsp, true, "/other")) };
        if (scenario == "volume") inventory = inventory with { Devices = inventory.Devices.Select(d => d.Key != Source ? d :
            d with { Volumes = d.Volumes.Add(d.Volumes[0] with { Key = new(Source, "second"), Root = "/second" }) }).ToImmutableArray() };
        if (scenario == "invalid-path") game = game with { Location = "/../escape.zip" };
        if (scenario == "unavailable-preference") state = DeviceAssociationRules.ChooseSource(state, SourceGroup.Removable, OtherSource);
        var result = GameCopySubmissionRules.Capture([new(Guid.NewGuid(), game)], inventory, state, MountedPathSyntax.Linux);
        Assert.Empty(result.Accepted); Assert.Single(result.Incidents);
    }

    [Fact]
    public void Explicit_volume_and_windows_roots_use_captured_host_syntax()
    {
        var inventory = new DeviceInventorySnapshot(2, [Device(Source, false, "D:\\source\\"), Device(Psp, true, "E:\\")]);
        var result = GameCopySubmissionRules.Capture([new(Guid.NewGuid(), Game, SourceVolume: new(Source, "volume"))],
            inventory, new(), MountedPathSyntax.Windows);
        Assert.Empty(result.Incidents); Assert.Equal("E:\\", Assert.Single(result.Accepted).Destination.Root);
        Assert.Equal(MountedPathSyntax.Windows, result.Accepted[0].Source.Syntax);
    }

    [Fact]
    public void Invalid_or_duplicate_operation_ids_are_individual_incidents()
    {
        var id = Guid.NewGuid();
        var result = GameCopySubmissionRules.Capture([new(id, Game), new(id, Game), new(Guid.Empty, Game)], Inventory, new(), MountedPathSyntax.Linux);
        Assert.Single(result.Accepted); Assert.Equal(2, result.Incidents.Length);
        Assert.All(result.Incidents, e => Assert.Equal(GameCopyErrorCode.InvalidSubmission, e.Error.Code));
    }

    [Theory]
    [InlineData("source-state")]
    [InlineData("psp-state")]
    [InlineData("psp-volumes")]
    [InlineData("duplicate-source")]
    [InlineData("duplicate-psp")]
    [InlineData("foreign-volume")]
    public void Capture_rejects_unready_or_nonunique_device_evidence(string scenario)
    {
        var inventory = Inventory;
        if (scenario == "source-state") inventory = inventory with { Devices = inventory.Devices.SetItem(0,
            inventory.Devices[0] with { State = DeviceState.Error }) };
        if (scenario == "psp-state") inventory = inventory with { Devices = inventory.Devices.SetItem(1,
            inventory.Devices[1] with { State = DeviceState.Mounting }) };
        if (scenario == "psp-volumes") inventory = inventory with { Devices = inventory.Devices.SetItem(1,
            inventory.Devices[1] with { Volumes = inventory.Devices[1].Volumes.Add(inventory.Devices[1].Volumes[0] with { Key = new(Psp, "second") }) }) };
        if (scenario == "duplicate-source") inventory = inventory with { Devices = inventory.Devices.Add(inventory.Devices[0]) };
        if (scenario == "duplicate-psp") inventory = inventory with { Devices = inventory.Devices.Add(inventory.Devices[1]) };
        var result = GameCopySubmissionRules.Capture([new(Guid.NewGuid(), Game, Source,
            scenario == "foreign-volume" ? new(OtherSource, "volume") : null)], inventory, new(), MountedPathSyntax.Linux);
        Assert.Empty(result.Accepted); Assert.Single(result.Incidents);
    }

    [Fact]
    public void Reservation_rejection_preserves_the_old_claim_and_unsettled_retry_charge()
    {
        var initial = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), 60, 100);
        var write = initial.State.RecordWritten(initial.Reservation!, 20);
        var failed = write.State.Reserve(write.Reservation.OperationId, Volume(), 81, 100);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, failed.Error!.Code);
        Assert.Same(write.State, failed.State); Assert.True(failed.State.IsCurrent(write.Reservation));
        var partial = write.State.SettleWritten(write.Reservation, 10);
        Assert.Equal(10, partial.Reservation.UnsettledBytes);
        Assert.Equal(40, partial.Reservation.PendingBytes);
        Assert.Throws<InvalidOperationException>(() => partial.State.Release(partial.Reservation, true));
    }

    [Fact]
    public void Reservations_do_not_mutate_the_prior_state_or_double_count_an_operations_replacement()
    {
        var empty = GameCopySpaceReservations.Empty; var id = Guid.NewGuid(); var other = Guid.NewGuid();
        var first = empty.Reserve(id, Volume(), 60, 100);
        Assert.Null(first.Error); Assert.Empty(empty.Reservations);
        var blocked = first.State.Reserve(other, Volume(), 41, 100);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, blocked.Error!.Code); Assert.Same(first.State, blocked.State);
        var exact = first.State.Reserve(other, Volume(), 40, 100);
        Assert.Null(exact.Error);
        var replaced = exact.State.Reserve(id, Volume(), 60, 100);
        Assert.Null(replaced.Error); Assert.Equal(2, replaced.State.Reservations.Length);
        Assert.False(replaced.State.IsCurrent(first.Reservation!));
    }

    [Theory]
    [InlineData(-1L, 100L, GameCopyErrorCode.InvalidActualSize)]
    [InlineData(1L, -1L, GameCopyErrorCode.UnknownFreeSpace)]
    [InlineData(1, null, GameCopyErrorCode.UnknownFreeSpace)]
    [InlineData(101L, 100L, GameCopyErrorCode.InsufficientSpace)]
    public void Invalid_capacity_returns_an_error_without_claims(long pending, long? free, GameCopyErrorCode error)
    {
        var result = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), pending, free);
        Assert.Equal(error, result.Error!.Code); Assert.Empty(result.State.Reservations); Assert.Null(result.Reservation);
    }

    [Fact]
    public void Checked_totals_reject_overflow_and_zero_byte_claims_are_valid()
    {
        var first = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), long.MaxValue, long.MaxValue);
        Assert.Null(first.Error);
        var overflow = first.State.Reserve(Guid.NewGuid(), Volume(), 1, long.MaxValue);
        Assert.Equal(GameCopyErrorCode.SizeOverflow, overflow.Error!.Code);
        var zero = first.State.Reserve(Guid.NewGuid(), Volume(), 0, long.MaxValue);
        Assert.Null(zero.Error);
    }

    [Fact]
    public void Writes_remain_charged_until_native_settlement_and_retained_output_is_not_refunded()
    {
        var first = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), 60, 100);
        var written = first.State.RecordWritten(first.Reservation!, 30);
        Assert.Equal(30, written.Reservation.PendingBytes); Assert.Equal(30, written.Reservation.UnsettledBytes);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace,
            written.State.Reserve(Guid.NewGuid(), Volume(), 41, 100).Error!.Code);
        var settled = written.State.SettleWritten(written.Reservation, 30);
        Assert.Null(settled.State.Reserve(Guid.NewGuid(), Volume(), 40, 70).Error);
        var closed = settled.State.Release(settled.Reservation, filesClosed: true);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, closed.Reserve(Guid.NewGuid(), Volume(), 71, 70).Error!.Code);
        Assert.Null(closed.Reserve(Guid.NewGuid(), Volume(), 70, 70).Error);
    }

    [Fact]
    public void Retry_reserves_full_failed_entry_again_and_skip_releases_only_pending_bytes()
    {
        var start = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), 60, 100);
        var written = start.State.RecordWritten(start.Reservation!, 20);
        var settled = written.State.SettleWritten(written.Reservation, 20);
        var retry = settled.State.Reserve(settled.Reservation.OperationId, Volume(), 60, 80);
        Assert.Null(retry.Error); Assert.Equal(60, retry.Reservation!.PendingBytes);
        var skipped = retry.State.SkipPending(retry.Reservation, 30);
        Assert.Equal(30, skipped.Reservation.PendingBytes);
        Assert.Null(skipped.State.Reserve(Guid.NewGuid(), Volume(), 50, 80).Error);
    }

    [Fact]
    public void Different_psps_and_generations_are_independent_but_reused_paths_are_not_identity()
    {
        var a = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), 100, 100);
        Assert.Null(a.State.Reserve(Guid.NewGuid(), Volume(OtherPsp), 100, 100).Error);
        Assert.Null(a.State.Reserve(Guid.NewGuid(), Volume(generation: "replacement"), 100, 100).Error);
        Assert.Equal(GameCopyErrorCode.InsufficientSpace, a.State.Reserve(Guid.NewGuid(), Volume(root: "/other-mount"), 1, 100).Error!.Code);
        Assert.Throws<InvalidOperationException>(() => a.State.Reserve(a.Reservation!.OperationId, Volume(OtherPsp), 1, 100));
    }

    [Fact]
    public void Stale_claims_and_unsafe_release_cannot_change_the_ledger()
    {
        var first = GameCopySpaceReservations.Empty.Reserve(Guid.NewGuid(), Volume(), 10, 20);
        var next = first.State.RecordWritten(first.Reservation!, 3);
        Assert.Throws<InvalidOperationException>(() => next.State.SkipPending(first.Reservation!, 1));
        Assert.Throws<InvalidOperationException>(() => next.State.Release(next.Reservation, true));
        Assert.Throws<InvalidOperationException>(() => next.State.Release(next.Reservation, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => next.State.RecordWritten(next.Reservation, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => next.State.SettleWritten(next.Reservation, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => next.State.SkipPending(next.Reservation, -1));
        var settled = next.State.SettleWritten(next.Reservation, 3);
        var released = settled.State.Release(settled.Reservation, true);
        var again = released.Reserve(first.Reservation!.OperationId, Volume(), 10, 17);
        Assert.False(again.State.IsCurrent(first.Reservation));
    }

    [Fact]
    public void Row_revisions_reject_cross_operation_stale_and_post_terminal_updates()
    {
        var request = Assert.Single(GameCopySubmissionRules.Capture([new(Guid.NewGuid(), Game)], Inventory, new(), MountedPathSyntax.Linux).Accepted);
        var row = new GameCopyOperationSnapshot(request, 1, new(request.OperationId, request.Game.Title, "/psp/ISO", GameCopyState.Preparing));
        var waiting = row with { Revision = 2, Snapshot = row.Snapshot with { State = GameCopyState.WaitingForResource,
            WaitReason = GameCopyWaitReason.DestinationFile } };
        Assert.True(GameCopySubmissionRules.CanApply(row, waiting));
        Assert.False(GameCopySubmissionRules.CanApply(waiting, row));
        Assert.False(GameCopySubmissionRules.CanApply(row, waiting with { Request = request with { OperationId = Guid.NewGuid() } }));
        Assert.False(GameCopySubmissionRules.CanApply(row, waiting with { Request = request with { Game = request.Game with { Title = "Other" } } }));
        Assert.False(GameCopySubmissionRules.CanApply(row, waiting with { Snapshot = waiting.Snapshot with { OperationId = Guid.NewGuid() } }));
        var final = waiting with { Revision = 3, Snapshot = waiting.Snapshot with { State = GameCopyState.Cancelled } };
        Assert.False(GameCopySubmissionRules.CanApply(final, waiting with { Revision = 4 }));
        var prompt = new GameCopyPrompt(request.OperationId, Guid.NewGuid());
        Assert.True(GameCopyRules.MatchesPrompt(row.Snapshot with { State = GameCopyState.AwaitingOverwrite, Prompt = prompt }, prompt));
        Assert.False(GameCopyRules.MatchesPrompt(waiting.Snapshot with { Prompt = prompt }, prompt));
    }
}
