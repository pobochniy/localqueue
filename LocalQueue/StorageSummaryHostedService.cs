using LocalQueue.Diagnostics;
using LocalQueue.Processing;
using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalQueue;

internal class StorageSummaryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyCollection<string> _commandTypes;
    private readonly ILogger<StorageSummaryHostedService> _logger;

    public StorageSummaryHostedService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IRawCommandHandler> commandHandlers,
        ILogger<StorageSummaryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _commandTypes = commandHandlers.Select(h => h.CommandType).ToHashSet();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LocalQueueMetrics.InitializeCommandTypes(_commandTypes);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15_000, stoppingToken);
                using var scope = _scopeFactory.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<ICommandsStorage>();
                var summary = await storage.GetSummary(stoppingToken);

                LocalQueueMetrics.RecordStorageSummary(summary);
            }
            catch (OperationCanceledException e) when (e.CancellationToken == stoppingToken)
            {
                // it's ok
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when processing storage summary");
            }
        }
    }
}