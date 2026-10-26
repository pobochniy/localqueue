using LocalQueue.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace LocalQueue;

internal class StorageSummaryHostedService(StorageSummaryCollector storageSummaryCollector) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => storageSummaryCollector.Run(stoppingToken), stoppingToken);
    }
}
