using System.Diagnostics.Metrics;
using LocalQueue.Diagnostics;

namespace LocalQueue.Tests.Diagnostics;

public class LocalQueueMetricsTests
{
    internal const string QueueLengthInstrumentName = "local-command-queue-length";
    private const string MaxTryCountInstrumentName = "local-command-queue-max-try-count";

    [Test]
    public void ShouldKeepSummaryOfOtherDatasource()
    {
        var datasource = $"datasource_{Guid.NewGuid():N}";
        var otherDatasource = $"datasource_{Guid.NewGuid():N}";

        LocalQueueMetrics.RecordStorageSummary(datasource, [Summary("Command", count: 5, maxTryCount: 2)]);
        LocalQueueMetrics.RecordStorageSummary(otherDatasource, [Summary("Command", count: 3, maxTryCount: 1)]);

        Assert.That(Observe(QueueLengthInstrumentName)[(datasource, "Command")], Is.EqualTo(5));
        Assert.That(Observe(MaxTryCountInstrumentName)[(datasource, "Command")], Is.EqualTo(2));
        Assert.That(Observe(QueueLengthInstrumentName)[(otherDatasource, "Command")], Is.EqualTo(3));
    }

    [Test]
    public void ShouldResetCommandTypesMissingInSummaryOfTheSameDatasource()
    {
        var datasource = $"datasource_{Guid.NewGuid():N}";

        LocalQueueMetrics.RecordStorageSummary(datasource, [Summary("Command", count: 5, maxTryCount: 2)]);
        LocalQueueMetrics.RecordStorageSummary(datasource, []);

        Assert.That(Observe(QueueLengthInstrumentName)[(datasource, "Command")], Is.EqualTo(0));
        Assert.That(Observe(MaxTryCountInstrumentName)[(datasource, "Command")], Is.EqualTo(0));
    }

    private static LocalQueueStorageSummary Summary(string commandType, int count, int maxTryCount)
    {
        return new LocalQueueStorageSummary { CommandType = commandType, Count = count, MaxTryCount = maxTryCount };
    }

    internal static Dictionary<(string DataSource, string CommandType), int> Observe(string instrumentName)
    {
        var measurements = new Dictionary<(string, string), int>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == Instruments.MeterName && instrument.Name == instrumentName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            string? dataSource = null;
            string? commandType = null;
            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "dataSource":
                        dataSource = (string?)tag.Value;
                        break;
                    case "commandType":
                        commandType = (string?)tag.Value;
                        break;
                }
            }

            measurements[(dataSource!, commandType!)] = value;
        });
        listener.Start();
        listener.RecordObservableInstruments();

        return measurements;
    }
}
