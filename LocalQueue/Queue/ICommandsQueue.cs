using System.Data;

namespace LocalQueue.Queue;

/// <summary>
/// Interface represents command queue
/// </summary>
public interface ICommandsQueue
{
    /// <summary>
    /// Enqueue commands for async processing
    /// </summary>
    /// <param name="connection">Connection on which operation will be executed. </param>
    /// <param name="transaction">Transaction in which operation will be executed. </param>
    /// <param name="commands">Commands to enqueue. </param>
    /// <param name="ct">A token to cancel the operation. </param>
    Task Enqueue<TCommand>(IDbConnection connection, IDbTransaction transaction, IEnumerable<TCommand> commands,
        CancellationToken ct)  where TCommand : notnull;
}