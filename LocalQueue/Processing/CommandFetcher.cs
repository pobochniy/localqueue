using System.Threading.Channels;
using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalQueue.Processing;

internal class CommandFetcher
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> _storageFactory;
    private readonly CommandProcessingOptions _options;
    private readonly IReadOnlyCollection<string> _commandTypes;
    private readonly ChannelWriter<CommandRecord> _fetchWriter;
    private readonly ILogger<CommandFetcher> _logger;

    public CommandFetcher(
        IServiceScopeFactory serviceScopeFactory,
        Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> storageFactory,
        CommandProcessingOptions options,
        IReadOnlyCollection<string> commandTypes,
        FetchCommandChannel fetchChannel,
        ILogger<CommandFetcher> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _storageFactory = storageFactory;
        _options = options;
        _commandTypes = commandTypes;
        _fetchWriter = fetchChannel.Writer;
        _logger = logger;
    }

    public async Task Run(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _fetchWriter.WaitToWriteAsync(stoppingToken);

            var fetchedInIteration = false;
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var commandContexts = await _storageFactory(scope.ServiceProvider).Prefetch(
                    _options.PrefetchCount,
                    _options.InvisibilityTimeout,
                    _commandTypes,
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