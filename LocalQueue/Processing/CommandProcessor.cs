using System.Threading.Channels;
using LocalQueue.Diagnostics;
using LocalQueue.Storage;
using Microsoft.Extensions.Logging;

namespace LocalQueue.Processing;

internal class CommandProcessor
{
    private readonly ChannelReader<CommandRecord> _fetchReader;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly IEnumerable<IRawCommandHandler> _commandHandlers;

    public CommandProcessor(
        FetchCommandChannel fetchChannel,
        IEnumerable<IRawCommandHandler> commandHandlers,
        ILogger<CommandProcessor> logger)
    {
        _fetchReader = fetchChannel.Reader;
        _commandHandlers = PrepareHandlers(commandHandlers);
        _logger = logger;
    }

    private static IReadOnlyCollection<IRawCommandHandler> PrepareHandlers(IEnumerable<IRawCommandHandler> handlers)
    {
        var prepared = handlers.ToArray();
        var duplicates = prepared
            .GroupBy(h => h.CommandType)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (!duplicates.Any())
            return prepared;

        throw new InvalidOperationException(
            $"Handlers must not contain duplicates. Duplicates: {string.Join(",", duplicates)}");
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var commandRecord = await _fetchReader.ReadAsync(ct);
                await Handle(commandRecord, ct);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == ct)
            {
                // it's ok
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Command processing failed.");
            }
        }
    }

    private async Task Handle(CommandRecord commandRecord, CancellationToken ct)
    {
        using var activity = LocalQueueDiagnostics.StartActivity(nameof(Handle));
        var handler = _commandHandlers.SingleOrDefault(x => x.CanHandle(commandRecord));
        if (handler == null)
        {
            _logger.LogError("Command handler for {Type} not found.", commandRecord.CommandType);
            return;
        }
        await handler.Handle(commandRecord, ct);
    }
}