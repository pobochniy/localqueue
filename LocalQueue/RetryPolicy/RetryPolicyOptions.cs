namespace LocalQueue.RetryPolicy;

/// <summary>
/// Options to setup retry policy.
/// </summary>
public class RetryPolicyOptions
{
    /// <summary>
    /// Number of retries to process failing command per prefetch.
    /// <remarks>Default is 3</remarks>
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
    /// <summary>
    /// Backoff interval before next retry.
    /// <remarks>Default is 500ms</remarks>
    /// </summary>
    public TimeSpan BackoffInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}