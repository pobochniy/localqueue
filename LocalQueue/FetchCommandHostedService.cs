using System.Threading.Channels;
using LocalQueue.Processing;
using LocalQueue.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalQueue;

internal class FetchCommandHostedService : BackgroundService
{
    private readonly ICommandsStorage _queueCommandsStorage;
    private readonly CommandProcessingOptions _options;
    private readonly ChannelWriter<CommandRecord> _fetchWriter;
    private readonly ILogger<FetchCommandHostedService> _logger;

    public FetchCommandHostedService(
        ICommandsStorage queueCommandsStorage,
        CommandProcessingOptions options,
        FetchCommandChannel  fetchChannel,
        ILogger<FetchCommandHostedService> logger)
    {
        _queueCommandsStorage = queueCommandsStorage;
        _options = options;
        _fetchWriter = fetchChannel.Writer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _fetchWriter.WaitToWriteAsync(stoppingToken);

            var fetchedInIteration = false;
            try
            {
                var commandContexts = await _queueCommandsStorage.Prefetch(
                    _options.PrefetchCount,
                    _options.InvisibilityTimeout,
                    stoppingToken);

                foreach (var command in commandContexts)
                {
                    fetchedInIteration = true;
                    await _fetchWriter.WriteAsync(command, stoppingToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to fetch commands");
            }
            finally
            {
                if (!fetchedInIteration)
                {
                    await Task.Delay(_options.IdleTimeout, stoppingToken);
                }
            }
        }
    }
}