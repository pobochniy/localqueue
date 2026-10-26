using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalQueue.Diagnostics;

/// <summary>
/// Storage of a worker and the command types it has handlers for
/// </summary>
internal record StorageSummarySource(
    Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> StorageFactory,
    IReadOnlyCollection<string> CommandTypes);

/// <summary>
/// Collects the summary of the storages of all the workers of the application
/// </summary>
internal class StorageSummaryCollector
{
    private readonly IReadOnlyCollection<StorageSummarySource> _sources;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<StorageSummaryCollector> _logger;
    private readonly TimeSpan _interval;

    // Keyed by the data source name: a storage factory may create a new storage that is not equal on every tick
    private readonly Dictionary<string, HashSet<string>> _loggedUnhandledCommandTypes = new();

    internal StorageSummaryCollector(
        IEnumerable<StorageSummarySource> sources,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<StorageSummaryCollector> logger,
        TimeSpan? interval = null)
    {
        _sources = sources.ToArray();
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(15);
    }

    internal async Task Run(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                try
                {
                    await RecordStorageSummaries(stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(e, "Error when processing storage summary.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // it's ok: stopping
        }
    }

    private async Task RecordStorageSummaries(CancellationToken ct)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var storages = new List<(ILocalCommandsQueueWorkerStorage Storage, StorageSummarySource Source)>();
        foreach (var source in _sources)
        {
            try
            {
                storages.Add((source.StorageFactory(scope.ServiceProvider), source));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when creating storage to process storage summary.");
            }
        }

        var storageGroups = storages
            .GroupBy(x => x.Storage, x => x.Source.CommandTypes)
            .Select(group => (Storage: group.Key, CommandTypes: group.SelectMany(t => t).ToHashSet()))
            .ToArray();
        foreach (var group in storageGroups)
        {
            LocalQueueMetrics.InitializeCommandTypes(group.Storage.DataSourceName, group.CommandTypes);
        }

        var summaries = await Task.WhenAll(storageGroups
            .Select(group => GetStorageSummary(group.Storage, ct)));

        // Storages that are not equal can share the data source name (the same table on different servers):
        // their summaries are recorded together, otherwise they overwrite each other's gauges
        foreach (var dataSource in storageGroups
                     .Zip(summaries, (group, summary) => (group.Storage.DataSourceName, Summary: summary))
                     .GroupBy(x => x.DataSourceName))
        {
            if (dataSource.Any(x => x.Summary == null)) continue;
            LocalQueueMetrics.RecordStorageSummary(dataSource.Key, Merge(dataSource.SelectMany(x => x.Summary!)));
        }

        if (storages.Count < _sources.Count) return;

        foreach (var dataSource in storageGroups
                     .Zip(summaries, (group, summary) => (Group: group, Summary: summary))
                     .GroupBy(x => x.Group.Storage.DataSourceName))
        {
            if (dataSource.Any(x => x.Summary == null)) continue;
            var unhandledCommandTypes = dataSource
                .SelectMany(x => x.Summary!
                    .Select(s => s.CommandType)
                    .Where(t => !x.Group.CommandTypes.Contains(t)))
                .ToHashSet();
            LogUnhandledCommandTypes(dataSource.Key, unhandledCommandTypes);
        }
    }

    private async Task<LocalQueueStorageSummary[]?> GetStorageSummary(
        ILocalCommandsQueueWorkerStorage storage, CancellationToken ct)
    {
        try
        {
            return (await storage.GetSummary(ct)).ToArray();
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(e, "Error when processing storage summary of {DataSource}.", storage.DataSourceName);
            return null;
        }
    }

    private static LocalQueueStorageSummary[] Merge(IEnumerable<LocalQueueStorageSummary> summary)
    {
        return summary
            .GroupBy(x => x.CommandType)
            .Select(g => new LocalQueueStorageSummary
            {
                CommandType = g.Key,
                Count = g.Sum(x => x.Count),
                MaxTryCount = g.Max(x => x.MaxTryCount)
            })
            .ToArray();
    }

    private void LogUnhandledCommandTypes(string dataSourceName, HashSet<string> unhandledCommandTypes)
    {
        var loggedCommandTypes = _loggedUnhandledCommandTypes.GetValueOrDefault(dataSourceName) ?? [];
        var newCommandTypes = unhandledCommandTypes.Except(loggedCommandTypes).ToArray();
        if (newCommandTypes.Length > 0)
        {
            _logger.LogWarning(
                "Command handlers for {Types} not found in the workers, their commands stay in {DataSource} "
                + "unless another application processes them.",
                newCommandTypes, dataSourceName);
        }

        _loggedUnhandledCommandTypes[dataSourceName] = unhandledCommandTypes;
    }
}
