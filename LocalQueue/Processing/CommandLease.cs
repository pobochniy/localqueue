using Microsoft.Extensions.Logging;

namespace LocalQueue.Processing;

/// <summary>
/// <see cref="ICommandLease"/> of the command the current async flow is processing.
/// </summary>
/// <remarks>
/// The worker sets <see cref="Current"/> before it calls the handler, so the lease is stateless and may be injected
/// into a handler of any lifetime, a singleton included: commands processed in parallel each see their own lock.
/// </remarks>
internal sealed class CommandLease : ICommandLease
{
    private static readonly AsyncLocal<CommandLeaseState?> CurrentState = new();

    private readonly ILogger<CommandLease> _logger;

    public CommandLease(ILogger<CommandLease> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The lock on the command of the current async flow; the worker sets it for the time of processing.
    /// </summary>
    internal static CommandLeaseState? Current
    {
        get => CurrentState.Value;
        set => CurrentState.Value = value;
    }

    public Task Extend(TimeSpan duration, CancellationToken ct)
    {
        if (Current is { } state)
        {
            return state.Extend(duration, ct);
        }

        _logger.LogWarning(
            "ICommandLease.Extend is called outside of a command processed by a worker, the lock is not extended");
        return Task.CompletedTask;
    }
}
