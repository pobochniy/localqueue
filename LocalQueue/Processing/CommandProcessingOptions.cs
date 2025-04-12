using LocalQueue.RetryPolicy;

namespace LocalQueue.Processing;

/// <summary>
/// Options to setup command processing parameters
/// </summary>
public class CommandProcessingOptions
{
    /// <summary>
    /// Count of commands to be prefetched and locked for processing
    /// </summary>
    public int PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Number of workers to be run to process commands in parallel.
    /// <remarks>Default is 10.</remarks> 
    /// </summary>
    public int WorkersCount { get; set; } = 10;

    /// <summary>
    /// Period command will be locked for from the moment it prefetched.
    /// <remarks>Default is 10.</remarks>
    /// </summary>
    public TimeSpan InvisibilityTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait before next prefetch after previous was empty.
    /// <remarks>Default is 3 seconds.</remarks>
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(3);

    internal Dictionary<string, RetryPolicyOptions> RetryOptions { get; set; } = new();
}