namespace LocalQueue.Processing;

/// <summary>
/// The lock on the command is over and another instance has prefetched the command, so the handler must stop.
/// </summary>
/// <remarks>
/// The worker stops processing the command without retries and without deleting it; the command is processed
/// again by the instance that has taken it.
/// </remarks>
public class CommandLeaseExpiredException : Exception
{
    /// <summary>
    /// The command the lock is over on.
    /// </summary>
    public Guid CommandId { get; }

    /// <summary>
    /// Create the exception for the command <paramref name="commandId"/>.
    /// </summary>
    public CommandLeaseExpiredException(Guid commandId)
        : base($"The lock on command {commandId} is over, another instance processes it")
    {
        CommandId = commandId;
    }
}
