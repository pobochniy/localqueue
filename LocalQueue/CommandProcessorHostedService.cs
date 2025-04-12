using LocalQueue.Processing;
using Microsoft.Extensions.Hosting;

namespace LocalQueue;

internal class CommandProcessorHostedService : BackgroundService
{
    private readonly CommandProcessor _commandProcessor;
    private readonly CommandProcessingOptions _options;

    public CommandProcessorHostedService(
        CommandProcessor commandProcessor,
        CommandProcessingOptions options)
    {
        _commandProcessor = commandProcessor;
        _options = options;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(Enumerable.Range(0, _options.WorkersCount)
            .Select(_ =>
                Task.Run(async () => { await _commandProcessor.ExecuteAsync(stoppingToken); }, stoppingToken)));
    }
}