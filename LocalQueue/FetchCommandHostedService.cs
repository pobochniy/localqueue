using LocalQueue.Processing;
using Microsoft.Extensions.Hosting;

namespace LocalQueue;

internal class FetchCommandHostedService : BackgroundService
{
    private readonly IEnumerable<CommandFetcher> _commandFetchers;

    public FetchCommandHostedService(IEnumerable<CommandFetcher> commandFetchers)
    {
        _commandFetchers = commandFetchers;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(_commandFetchers
            .Select(commandFetcher => Task.Run(() => commandFetcher.Run(stoppingToken), stoppingToken)));
    }
}