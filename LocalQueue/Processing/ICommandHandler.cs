namespace LocalQueue.Processing;

/// <summary>
/// Interface to define a command handler
/// </summary>
public interface ICommandHandler<in TCommand>
{
    /// <summary>
    /// Defines method to handle command.
    /// </summary>
    /// <param name="command">An command to handle. </param>
    /// <param name="ct">A cancellation token that can be used to cancel this operation.</param>
    /// <returns></returns>
    Task Handle(TCommand command, CancellationToken ct);
}