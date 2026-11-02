using LocalQueue.Storage;

namespace LocalQueue.Processing;

/// <summary>
/// The lock on one command as the worker knows it: taken at prefetch, moved by <see cref="Extend"/>.
/// </summary>
/// <remarks>
/// The storage decides whether the command is still held: every write is conditional on
/// <see cref="CommandRecord.TryCount"/> of the prefetch, which another prefetch increments. The local end of the lock
/// only decides whether a write is needed and whether to start another try. It is counted by the monotonic clock of
/// the instance from before each write to the storage, so it never ends later than the lock in the storage,
/// whatever the clock of the storage is.
/// </remarks>
internal sealed class CommandLeaseState
{
    private const long NoLock = long.MinValue;

    // Extensions of one command are written one by one: a shorter one must not overwrite a longer one
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Guid _id;
    private readonly int _tryCount;
    // Null once released: work the handler has left running keeps this state, but not the storage of its scope
    private volatile ILocalCommandsQueueWorkerStorage? _storage;

    // Environment.TickCount64, NoLock when the command came without a lock
    private long _lockedTillMs;

    // The command is prefetched by another instance, or its processing is over
    private volatile bool _released;

    public CommandLeaseState(CommandRecord commandRecord, ILocalCommandsQueueWorkerStorage storage)
    {
        _id = commandRecord.Id;
        _tryCount = commandRecord.TryCount;
        _storage = storage;
        _lockedTillMs = commandRecord.LocalLockedTillMs
                        // Only for a record not from CommandFetcher (tests): the lock by the clock of the storage
                        // is compared with the clock of the instance, so it is exact only for synchronized clocks
                        ?? (commandRecord.LockedTillUtc is { } lockedTillUtc
                            ? Environment.TickCount64 + (long)(lockedTillUtc - DateTime.UtcNow).TotalMilliseconds
                            : NoLock);
    }

    /// <summary>
    /// The storage has reported that the command is no longer held, or the processing is over.
    /// </summary>
    public bool IsReleased => _released;

    public bool IsExpired => _released
                             || (Volatile.Read(ref _lockedTillMs) is var lockedTill
                                 && lockedTill != NoLock && lockedTill < Environment.TickCount64);

    public async Task Extend(TimeSpan duration, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        // A lock for months is a mistake, and its milliseconds would overflow the storage arguments
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duration, ICommandLease.MaxDuration);

        await _gate.WaitAsync(ct);
        try
        {
            // Release sets the flag before it drops the storage: the storage is read first, the flag is checked after
            var storage = _storage;
            if (_released || storage is null)
            {
                throw new CommandLeaseExpiredException(_id);
            }

            // Taken before the update: the lock in the storage ends no earlier than this
            var now = Environment.TickCount64;
            var current = Volatile.Read(ref _lockedTillMs);
            if (current != NoLock && now + (long)duration.TotalMilliseconds <= current)
            {
                // The lock already ends later: Extend never shortens it, and a handler extending it before every
                // short step writes to the storage once per a half of the duration, not on every step
                return;
            }

            // Locked for a half more than asked, so the next extensions skip the write for that half
            var lockedFor = duration + duration / 2;

            // A lock that is over locally is still extended while no other instance has prefetched the command
            if (!await storage.MakeInvisibleFor(_id, _tryCount, lockedFor, ct))
            {
                _released = true;
                throw new CommandLeaseExpiredException(_id);
            }

            Volatile.Write(ref _lockedTillMs, now + (long)lockedFor.TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The processing is over: work the handler has left running must not lock the command any more.
    /// </summary>
    public void Release()
    {
        _released = true;
        _storage = null;
    }
}
