using System.Data;
using LocalQueue.Serialization;
using LocalQueue.Storage;

namespace LocalQueue.Queue;

internal class CommandsQueue : ICommandsQueue
{
    private readonly ILocalCommandsQueueStorage _storage;
    private readonly ICommandSerializer _serializer;

    public CommandsQueue(ILocalCommandsQueueStorage storage, ICommandSerializer serializer)
    {
        _storage = storage;
        _serializer = serializer;
    }

    public Task Enqueue<TCommand>(IDbConnection connection, IDbTransaction transaction,
        IEnumerable<TCommand> commands, CancellationToken ct) where TCommand : notnull
    {
        return Enqueue(connection, transaction, commands, null, ct);
    }

    public async Task Enqueue<TCommand>(IDbConnection connection, IDbTransaction transaction, IEnumerable<TCommand> commands,
        DateTime? processingStartTime, CancellationToken ct) where TCommand : notnull
    {
        var toStore = commands
            .Select(c => new CommandRecord
            {
                Id = Guid.NewGuid(),
                Data = _serializer.Serialize(c),
                CommandType = c.GetType().FullName!,
                CreatedAtUtc = DateTime.UtcNow,
                LockedTillUtc = processingStartTime
            })
            .ToList();

        if (!toStore.Any())
            return;

        await _storage.Create(connection, transaction, toStore, ct);
    }
}