using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using LocalQueue.Storage;

namespace LocalQueue.Diagnostics;

internal static class LocalQueueMetrics
{
    private static readonly ConcurrentDictionary<string, int> QueuedCommandsByType = new();
    private static readonly ConcurrentDictionary<string, int> MaxTryCountByType = new();
    private static readonly Meter Meter = new(Instruments.MeterName);

    private static readonly Histogram<double> CommandProcessingTimeHistogram = Meter.CreateHistogram<double>(
        Instruments.CommandProcessingTimeHistogramName,
        "seconds",
        "Command completion time in seconds by type");

    private static readonly Histogram<double> CommandProcessingLagHistogram = Meter.CreateHistogram<double>(
        Instruments.CommandProcessingLagHistogramName,
        "seconds",
        "Command completion lag in seconds by type from creation to completion");

    private static readonly Counter<int> CompletedCommandsCounter = Meter.CreateCounter<int>(
        "local-command-queue-completed",
        "count",
        "Number of command handled by type");

    static LocalQueueMetrics()
    {
        Meter.CreateObservableGauge(
            "local-command-queue-length",
            ObserveStorageQueue,
            "count",
            "Number of commands in storage by type");

        Meter.CreateObservableGauge(
            "local-command-queue-max-try-count",
            ObserveMaxTryCount,
            "count",
            "Count of command processing tries");
    }

    public static void InitializeCommandTypes(IEnumerable<string> commandTypes)
    {
        foreach (var commandType in commandTypes)
        {
            QueuedCommandsByType.TryAdd(commandType, 0);
            MaxTryCountByType.TryAdd(commandType, 0);
        }
    }

    private static IEnumerable<Measurement<int>> ObserveStorageQueue()
    {
        return QueuedCommandsByType.Select(kv => new Measurement<int>(
            kv.Value, new KeyValuePair<string, object?>("commandType", kv.Key)));
    }

    private static IEnumerable<Measurement<int>> ObserveMaxTryCount()
    {
        return MaxTryCountByType.Select(kv => new Measurement<int>(
            kv.Value, new KeyValuePair<string, object?>("commandType", kv.Key)));
    }

    internal static void RecordStorageSummary(IEnumerable<LocalQueueStorageSummary> summary)
    {
        foreach (var key in QueuedCommandsByType.Keys)
        {
            QueuedCommandsByType[key] = 0;
        }

        foreach (var key in MaxTryCountByType.Keys)
        {
            MaxTryCountByType[key] = 0;
        }

        foreach (var summaryEntry in summary)
        {
            QueuedCommandsByType[summaryEntry.CommandType] = summaryEntry.Count;
            MaxTryCountByType[summaryEntry.CommandType] = summaryEntry.MaxTryCount;
        }
    }

    internal static void RecordPostProcessingMetrics(CommandRecord commandContext, TimeSpan processingTime,
        TimeSpan processingLag)
    {
        var tags = BuildTags(commandContext);

        CommandProcessingTimeHistogram.Record(processingTime.TotalSeconds, tags);
        CommandProcessingLagHistogram.Record(processingLag.TotalSeconds, tags);
        CompletedCommandsCounter.Add(1, tags);
    }

    private static KeyValuePair<string, object?>[] BuildTags(CommandRecord commandContext)
    {
        return new[]
        {
            new KeyValuePair<string, object?>("commandType", commandContext.CommandType)
        };
    }
}