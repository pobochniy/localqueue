namespace LocalQueue.Processing;

/// <summary>
/// The command processing function to be executed asynchronously.
/// </summary>
public delegate Task FuncCommandHandler<in TCommand>(IServiceProvider provider, TCommand command, CancellationToken ct);