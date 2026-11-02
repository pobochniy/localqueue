namespace LocalQueue.Processing;

/// <summary>
/// The lock on the command the handler is processing.
/// </summary>
/// <remarks>
/// A prefetched command is invisible for other workers for <see cref="CommandProcessingOptions.InvisibilityTimeout"/>.
/// A handler that may run longer than that, a long chain of slow steps for example, extends the lock between
/// the steps instead of setting the timeout for the worst case: then a command left by a crashed instance comes
/// back after one step rather than after the longest possible processing time.
/// Inject it into the handler of any lifetime: the lease follows the command being processed in the current
/// async flow. Outside of a worker (the handler is called directly) there is no command to lock,
/// <see cref="Extend"/> logs a warning and does nothing.
/// </remarks>
public interface ICommandLease
{
    /// <summary>
    /// The longest <see cref="Extend"/> may lock the command for at once.
    /// </summary>
    static TimeSpan MaxDuration => TimeSpan.FromDays(14);

    /// <summary>
    /// Keep the command invisible for other workers for at least <paramref name="duration"/> from now.
    /// </summary>
    /// <remarks>
    /// The lock is never shortened: when it already ends later, nothing is changed. When it ends earlier, it is taken
    /// for one and a half of <paramref name="duration"/>, so calls before every short step write to the storage
    /// once per a half of <paramref name="duration"/>. Extend before the lock is over
    /// and with a margin: once it is over, another instance may prefetch the command and process it in parallel.
    /// If the processing fails after the extension, the command comes back when the extended lock is over,
    /// unless the retry policy hides it for another time.
    /// </remarks>
    /// <param name="duration">
    /// How long from now the command stays locked, positive and at most <see cref="MaxDuration"/>.
    /// </param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="CommandLeaseExpiredException">
    /// Another instance has prefetched the command, or the processing is over: the handler must stop.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="duration"/> is not positive or longer than <see cref="MaxDuration"/>.
    /// </exception>
    Task Extend(TimeSpan duration, CancellationToken ct);
}
