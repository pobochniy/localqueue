namespace LocalQueue.Storage;

/// <summary>
/// Dto to save command into storage
/// </summary>
public class CommandRecord
{
    /// <summary>
    /// Unique command id.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Command type.
    /// </summary>
    public string CommandType { get; init; } = null!;

    /// <summary>
    /// Serialized command json.  
    /// </summary>
    public string Data { get; init; } = null!;

    /// <summary>
    /// The date time command was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// The date time till command is locked. 
    /// </summary>
    public DateTime? LockedTillUtc { get; init; }

    /// <summary>
    /// The number of command is processing iteration. 
    /// </summary>
    public int TryCount { get; init; }

    internal bool IsLockExpired => LockedTillUtc < DateTime.UtcNow;
}