namespace LocalQueue.Processing;

/// <summary>
/// Retry policy for one command type. <see cref="RetryPolicy.RetryPolicyOptions"/> is the fixed-value default.
/// </summary>
public interface ICommandRetryPolicy
{
    /// <summary>
    /// Number of tries to process failing command per prefetch; 1 means no retries. Below 1 the command is
    /// not processed at all and is rescheduled as a failure.
    /// </summary>
    int MaxRetryCount { get; }

    /// <summary>
    /// Backoff interval before next retry per prefetch.
    /// </summary>
    /// <param name="retry">1-based retry number within the current prefetch.</param>
    TimeSpan BackoffFor(int retry);

    /// <summary>
    /// Delay next prefetch for; null — do not reschedule.
    /// </summary>
    /// <param name="attempt"><see cref="Storage.CommandRecord.TryCount"/> of the command.</param>
    TimeSpan? RedeliverAfter(int attempt);

    /// <summary>
    /// Number of deliveries after which the command is given up; null — never.
    /// </summary>
    /// <remarks>
    /// A delivery is a prefetch of the command, so a delivery the instance didn't finish (the process was killed in
    /// the middle of the command) counts too. The command is given up when its last delivery fails and when it is
    /// prefetched beyond the limit: the handler doesn't run, the error is logged, the
    /// <c>local-command-queue-given-up</c> counter grows, and the command stays in the table locked for
    /// <see cref="GivenUpLock"/>, for an investigation. To process it again, reset its lock and try count
    /// (<c>locked_till_utc = null, try_count = 0</c>).
    /// </remarks>
    int? MaxAttempts => null;

    /// <summary>
    /// How long the given up command is locked for: for good.
    /// </summary>
    static readonly TimeSpan GivenUpLock = TimeSpan.FromDays(365 * 100);
}
