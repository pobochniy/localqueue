using LocalQueue.Diagnostics;

namespace LocalQueue.Storage;

/// <summary>
/// Commands queue worker storage interface.
/// </summary>
/// <remarks>
/// Storages of the same table must be equal (override <see cref="object.Equals(object)"/> and
/// <see cref="object.GetHashCode"/>) if a storage is created more than once: by a factory or for several workers.
/// The storage summary is collected once for equal storages, the summaries of storages that are not equal are summed
/// up by <see cref="DataSourceName"/>, so the commands of the same table are counted more than once, and the command
/// types handled by the workers of one storage are logged as having no handlers by the others.
/// </remarks>
public interface ILocalCommandsQueueWorkerStorage
{
    /// <summary>
    /// Prefetch specified number of commands of the specified types and lock them for specified timeout
    /// to be invisible for other instances
    /// </summary>
    /// <param name="count">Number of commands to be prefetched.</param>
    /// <param name="timeout">Lock timeout.</param>
    /// <param name="commandTypes">Command types to prefetch, the types the worker has handlers for. Must not be empty.</param>
    /// <param name="ct">A token to cancel the operation. </param>
    Task<IEnumerable<CommandRecord>> Prefetch(int count, TimeSpan timeout, IReadOnlyCollection<string> commandTypes,
        CancellationToken ct);

    /// <summary>
    /// Delete completed command from storage, if it is still held by the worker that prefetched it.
    /// </summary>
    /// <remarks>
    /// Every prefetch increments <see cref="CommandRecord.TryCount"/>: the command is deleted only while its
    /// <c>TryCount</c> equals <paramref name="tryCount"/>, so a command prefetched again by another instance after
    /// the lock is over stays for that instance.
    /// </remarks>
    /// <param name="id">Command id.</param>
    /// <param name="tryCount"><see cref="CommandRecord.TryCount"/> of the prefetched command.</param>
    /// <param name="ct">A token to cancel the operation. </param>
    /// <returns><c>false</c> when the command is prefetched by another instance or deleted already.</returns>
    Task<bool> Delete(Guid id, int tryCount, CancellationToken ct);

    /// <summary>
    /// Get statistics for commands in storage <see cref="LocalQueueStorageSummary"/>>  
    /// </summary>
    /// <param name="ct">A token to cancel the operation. </param>
    Task<IEnumerable<LocalQueueStorageSummary>> GetSummary(CancellationToken ct);

    /// <summary>
    /// Make command invisible for specified timeout from now, if it is still held by the worker that prefetched it.
    /// </summary>
    /// <remarks>
    /// The lock is set only while <see cref="CommandRecord.TryCount"/> equals <paramref name="tryCount"/>
    /// (see <see cref="Delete"/>); the check and the update must be one atomic statement.
    /// </remarks>
    /// <param name="id">Command id.</param>
    /// <param name="tryCount"><see cref="CommandRecord.TryCount"/> of the prefetched command.</param>
    /// <param name="timeout">Lock timeout.</param>
    /// <param name="ct">A token to cancel the operation. </param>
    /// <returns><c>false</c> when the command is prefetched by another instance or deleted already.</returns>
    Task<bool> MakeInvisibleFor(Guid id, int tryCount, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// The datasource name
    /// </summary>
    string DataSourceName { get; }
}