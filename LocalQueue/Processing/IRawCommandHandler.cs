using LocalQueue.Storage;

namespace LocalQueue.Processing;

internal interface IRawCommandHandler
{
    string CommandType { get; }
    bool CanHandle(CommandRecord commandRecord);
    Task Handle(CommandRecord commandRecord, CancellationToken ct);
}