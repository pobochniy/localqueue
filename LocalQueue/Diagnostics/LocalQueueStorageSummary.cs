namespace LocalQueue.Diagnostics;

/// <summary>
/// Represents statistic data for commands in storage by type  
/// </summary>
public class LocalQueueStorageSummary
{
    /// <summary>
    /// Command type name
    /// </summary>
    public string CommandType { get; init; } = null!;

    /// <summary>
    /// Count of commands in storage
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Maximum of processing tries
    /// </summary>
    public int MaxTryCount { get; set; }
}