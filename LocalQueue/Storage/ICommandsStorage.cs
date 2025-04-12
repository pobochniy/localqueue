using System.Data;
using LocalQueue.Diagnostics;

namespace LocalQueue.Storage;

/// <summary>
/// Commands queue storage interface.
/// </summary>
public interface ICommandsStorage
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

    /// <summary>
    /// Prefetch specified number of commands and lock them for specified timeout to be invisible for other instances
    /// </summary>
    /// <param name="count">Number of commands to be prefetched.</param>
    /// <param name="timeout">Lock timeout.</param>
    /// <param name="ct">A token to cancel the operation. </param>
    Task<IEnumerable<CommandRecord>> Prefetch(int count, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Delete completed command from storage.
    /// </summary>
    /// <param name="id">Command id.</param>
    /// <param name="ct">A token to cancel the operation. </param>
    Task Delete(Guid id, CancellationToken ct);

    /// <summary>
    /// Get statistics for commands in storage <see cref="LocalQueueStorageSummary"/>>  
    /// </summary>
    /// <param name="ct">A token to cancel the operation. </param>
    Task<IEnumerable<LocalQueueStorageSummary>> GetSummary(CancellationToken ct);
}