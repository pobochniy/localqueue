using LocalQueue.Processing;

namespace LocalQueue.RetryPolicy;

/// <summary>
/// Options to setup retry policy.
/// </summary>
public class RetryPolicyOptions : ICommandRetryPolicy
{
    /// <summary>
    /// Number of tries to process failing command per prefetch; 1 means no retries. Below 1 the command is
    /// not processed at all and is rescheduled as a failure.
    /// <remarks>Default is 3</remarks>
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
    /// <summary>
    /// Backoff interval before next retry per prefetch.
    /// <remarks>Default is 500ms</remarks>
    /// </summary>
    public TimeSpan BackoffInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    
    /// <summary>
    /// Delay next prefetch for.
    /// </summary>
    public TimeSpan? RetryAfterFailure { get; set; }

    /// <inheritdoc />
    public TimeSpan BackoffFor(int retry) => BackoffInterval;

    /// <inheritdoc />
    public TimeSpan? RedeliverAfter(int attempt) => RetryAfterFailure;
}