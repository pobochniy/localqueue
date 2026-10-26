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

    /// <summary>
    /// Make command invisible for specified timeout.
    /// </summary>
    /// <param name="id">Command id.</param>
    /// <param name="timeout">Lock timeout.</param>
    /// <param name="ct">A token to cancel the operation. </param>
    Task MakeInvisibleFor(Guid id, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// The datasource name
    /// </summary>
    string DataSourceName { get; }
}