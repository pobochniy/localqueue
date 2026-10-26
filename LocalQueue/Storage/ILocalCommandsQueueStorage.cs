using System.Data;

namespace LocalQueue.Storage;

/// <summary>
/// Commands queue storage interface.
/// </summary>
public interface ILocalCommandsQueueStorage
{
    /// <summary>
    /// Save commands to local queue.
    /// </summary>
    /// <param name="connection">Connection on which operation will be executed. </param>
    /// <param name="transaction">Transaction in which operation will be executed. </param>
    /// <param name="commands">The list of commands to save. </param>
    /// <param name="ct">A token to cancel the operation. </param>
    Task Create(IDbConnection connection, IDbTransaction transaction, IEnumerable<CommandRecord> commands,
        CancellationToken ct);
}