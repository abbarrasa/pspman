using System.Collections.Immutable;
using PSPMan.Core.Devices;

namespace PSPMan.Core.Transfers;

/// <summary>Volume incarnation, not its mount spelling. Native adapters still validate held evidence.</summary>
public sealed record CopyVolumeKey(DeviceKey Device, DeviceIdentity DeviceIdentity, string? MediaGeneration,
    VolumeKey Volume, DeviceIdentity VolumeIdentity)
{
    public static CopyVolumeKey From(CopyVolumeEvidence evidence) => new(evidence.Device, evidence.DeviceIdentity,
        evidence.MediaGeneration, evidence.Volume, evidence.VolumeIdentity);
}
public sealed record CopySpaceReservation(Guid OperationId, CopyVolumeKey Volume, long Version,
    long PendingBytes, long UnsettledBytes);
public sealed record CopySpaceReservationResult(GameCopySpaceReservations State,
    CopySpaceReservation? Reservation = null, GameCopyError? Error = null);
public sealed record CopySpaceReservationUpdate(GameCopySpaceReservations State, CopySpaceReservation Reservation);

/// <summary>
/// Immutable, checked budget transitions only. A shared resource owner must atomically publish the returned
/// state and obtain consistent native free-space/settlement samples. This class performs no I/O or scheduling.
/// </summary>
public sealed class GameCopySpaceReservations
{
    private readonly ImmutableDictionary<Guid, CopySpaceReservation> reservations;
    private readonly long version;
    private GameCopySpaceReservations(ImmutableDictionary<Guid, CopySpaceReservation> reservations, long version)
    { this.reservations = reservations; this.version = version; }
    public static GameCopySpaceReservations Empty { get; } = new(ImmutableDictionary<Guid, CopySpaceReservation>.Empty, 0);
    public ImmutableArray<CopySpaceReservation> Reservations => reservations.Values.ToImmutableArray();

    /// <summary>Replaces this operation's pending attempt, never duplicates it. Unsettled writes remain charged.</summary>
    public CopySpaceReservationResult Reserve(Guid operationId, CopyVolumeEvidence volume, long pendingBytes, long? availableBytes)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        ArgumentNullException.ThrowIfNull(volume);
        var key = CopyVolumeKey.From(volume);
        reservations.TryGetValue(operationId, out var previous);
        if (previous is not null && previous.Volume != key)
            throw new InvalidOperationException("An operation cannot change its captured volume.");
        if (pendingBytes < 0) return Failure(GameCopyErrorCode.InvalidActualSize, "Pending output size is invalid.");
        if (availableBytes is null or < 0) return Failure(GameCopyErrorCode.UnknownFreeSpace, "Available destination space could not be determined.");
        try
        {
            long charged = checked(pendingBytes + (previous?.UnsettledBytes ?? 0));
            foreach (var reservation in reservations.Values)
                if (reservation.OperationId != operationId && reservation.Volume == key)
                    charged = checked(charged + checked(reservation.PendingBytes + reservation.UnsettledBytes));
            if (charged > availableBytes)
                return Failure(GameCopyErrorCode.InsufficientSpace, "The destination does not have enough unreserved free space.");
            var nextVersion = checked(version + 1);
            var claim = new CopySpaceReservation(operationId, key, nextVersion, pendingBytes, previous?.UnsettledBytes ?? 0);
            return new(new(reservations.SetItem(operationId, claim), nextVersion), claim);
        }
        catch (OverflowException) { return Failure(GameCopyErrorCode.SizeOverflow, "Reserved output exceeds the supported byte range."); }
    }

    public bool IsCurrent(CopySpaceReservation reservation) => reservations.TryGetValue(reservation.OperationId, out var current)
        && current == reservation;

    /// <summary>Successful bytes move from pending to unsettled; they are not yet free reservation capacity.</summary>
    public CopySpaceReservationUpdate RecordWritten(CopySpaceReservation reservation, long bytes)
    {
        RequireCurrent(reservation); RequireAmount(bytes, reservation.PendingBytes);
        return Replace(reservation with { PendingBytes = reservation.PendingBytes - bytes,
            UnsettledBytes = checked(reservation.UnsettledBytes + bytes) });
    }

    public CopySpaceReservationUpdate SkipPending(CopySpaceReservation reservation, long bytes)
    {
        RequireCurrent(reservation); RequireAmount(bytes, reservation.PendingBytes);
        return Replace(reservation with { PendingBytes = reservation.PendingBytes - bytes });
    }

    /// <summary>Caller attests these bytes are reflected in a consistent held-volume capacity sample.</summary>
    public CopySpaceReservationUpdate SettleWritten(CopySpaceReservation reservation, long bytes)
    {
        RequireCurrent(reservation); RequireAmount(bytes, reservation.UnsettledBytes);
        return Replace(reservation with { UnsettledBytes = reservation.UnsettledBytes - bytes });
    }

    /// <summary>Releases unused claims only, not destination bytes. Closing and settlement precede release.</summary>
    public GameCopySpaceReservations Release(CopySpaceReservation reservation, bool filesClosed)
    {
        RequireCurrent(reservation);
        if (!filesClosed || reservation.UnsettledBytes != 0)
            throw new InvalidOperationException("Close operation files and settle written bytes before releasing the claim.");
        return new(reservations.Remove(reservation.OperationId), checked(version + 1));
    }

    private CopySpaceReservationUpdate Replace(CopySpaceReservation reservation)
    {
        var nextVersion = checked(version + 1);
        reservation = reservation with { Version = nextVersion };
        return new(new(reservations.SetItem(reservation.OperationId, reservation), nextVersion), reservation);
    }
    private void RequireCurrent(CopySpaceReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (!IsCurrent(reservation)) throw new InvalidOperationException("The reservation is no longer current.");
    }
    private static void RequireAmount(long bytes, long maximum)
    { if (bytes < 0 || bytes > maximum) throw new ArgumentOutOfRangeException(nameof(bytes)); }
    private CopySpaceReservationResult Failure(GameCopyErrorCode code, string message) => new(this, Error: new(code, message));
}
