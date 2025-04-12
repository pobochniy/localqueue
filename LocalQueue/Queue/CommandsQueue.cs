using System.Data;
using LocalQueue.Serialization;
using LocalQueue.Storage;

namespace LocalQueue.Queue;

internal class CommandsQueue : ICommandsQueue
{
    private readonly ICommandsStorage _storage;
    private readonly ICommandSerializer _serializer;

    public CommandsQueue(ICommandsStorage storage, ICommandSerializer serializer)
    {
        _storage = storage;
        _serializer = serializer;
    }

    public async Task Enqueue<TCommand>(IDbConnection connection, IDbTransaction transaction,
        IEnumerable<TCommand> commands, CancellationToken ct) where TCommand : notnull
    {
        var toStore = commands
            .Select(c => new CommandRecord
            {
                Id = Guid.NewGuid(),
                Data = _serializer.Serialize(c),
                CommandType = c.GetType().FullName!,
                CreatedAtUtc = DateTime.UtcNow
            })
            .ToList();

        if (!toStore.Any())
            return;

        await _storage.Create(connection, transaction, toStore, ct);
    }
}