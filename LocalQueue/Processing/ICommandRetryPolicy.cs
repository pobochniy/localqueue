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
}
