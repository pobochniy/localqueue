using LocalQueue.Processing;
using Microsoft.Extensions.Hosting;

namespace LocalQueue;

internal class CommandProcessorHostedService : BackgroundService
{
    private readonly IEnumerable<CommandProcessor> _commandProcessors;

    public CommandProcessorHostedService(IEnumerable<CommandProcessor> commandProcessors)
    {
        _commandProcessors = commandProcessors;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(_commandProcessors
            .Select(commandProcessor => Task.Run(() => commandProcessor.Run(stoppingToken), stoppingToken)));
    }
}