using LocalQueue.Diagnostics;
using LocalQueue.Storage;
using LocalQueue.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Moq;

namespace LocalQueue.Tests.Diagnostics;

public class StorageSummaryCollectorTests
{
    [Test]
    public async Task ShouldCollectSummaryOnceForWorkersOfTheSameStorage()
    {
        var logger = new ListLogger();
        var storage = StorageMock($"datasource_{Guid.NewGuid():N}");

        await RunHostedService(logger,
            Source(storage, "CommandA"),
            Source(storage, "CommandB"));

        storage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(logger.Messages, Is.Empty);
    }

    [Test]
    public async Task ShouldCollectSummaryOfEachStorage()
    {
        var logger = new ListLogger();
        var storage = StorageMock($"datasource_{Guid.NewGuid():N}");
        var otherStorage = StorageMock($"datasource_{Guid.NewGuid():N}");

        await RunHostedService(logger,
            Source(storage, "CommandA"),
            Source(otherStorage, "CommandB"));

        storage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.Once);
        otherStorage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ShouldCollectSummaryOfEachStorageWithTheSameDataSourceName()
    {
        var logger = new ListLogger();
        var dataSourceName = $"datasource_{Guid.NewGuid():N}";
        var storage = StorageMock(dataSourceName, "CommandA");
        var otherStorage = StorageMock(dataSourceName, "CommandB");

        await RunHostedService(logger,
            Source(storage, "CommandA"),
            Source(otherStorage, "CommandB"));

        storage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.Once);
        otherStorage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(logger.Messages, Is.Empty);
    }

    [Test]
    public async Task ShouldRecordSummaryOfStoragesWithTheSameDataSourceNameTogether()
    {
        var logger = new ListLogger();
        var dataSourceName = $"datasource_{Guid.NewGuid():N}";
        var storage = StorageMock(dataSourceName, "CommandA");
        var otherStorage = StorageMock(dataSourceName, "CommandA", "CommandB");

        await RunHostedService(logger,
            Source(storage, "CommandA", "CommandB"),
            Source(otherStorage, "CommandA", "CommandB"));

        var queueLength = LocalQueueMetricsTests.Observe(LocalQueueMetricsTests.QueueLengthInstrumentName);
        Assert.That(queueLength[(dataSourceName, "CommandA")], Is.EqualTo(2));
        Assert.That(queueLength[(dataSourceName, "CommandB")], Is.EqualTo(1));
    }

    [Test]
    public async Task ShouldLogCommandTypesWithoutHandlersOfStorageHandledByWorkerOfOtherStorage()
    {
        var logger = new ListLogger();
        var dataSourceName = $"datasource_{Guid.NewGuid():N}";
        var storage = StorageMock(dataSourceName, "CommandA");
        var otherStorage = StorageMock(dataSourceName, "CommandA");

        await RunHostedService(logger,
            Source(storage, "CommandA"),
            Source(otherStorage, "CommandB"));

        Assert.That(logger.Messages.Single(), Does.StartWith("Warning:").And.Contain("CommandA"));
    }

    [Test]
    public async Task ShouldCollectSummaryOfOtherStoragesWhenStorageFactoryFails()
    {
        var logger = new ListLogger();
        var storage = StorageMock($"datasource_{Guid.NewGuid():N}");

        await RunHostedService(logger,
            Source(storage, "CommandA"),
            FailingSource("CommandB"));

        storage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(logger.Messages.Single(), Does.StartWith("Error:"));
    }

    [Test]
    public async Task ShouldNotLogCommandTypesOfWorkerWithFailedStorageFactoryAsUnhandled()
    {
        var logger = new ListLogger();
        var storage = StorageMock($"datasource_{Guid.NewGuid():N}", "CommandA", "CommandB");

        await RunHostedService(logger,
            Source(storage, "CommandA"),
            FailingSource("CommandB"));

        Assert.That(logger.Messages.Where(x => x.StartsWith("Warning:")), Is.Empty);
    }

    [Test]
    public async Task ShouldLogCommandTypesWithoutHandlersOfAnyWorkerOnce()
    {
        var logger = new ListLogger();
        var storage = StorageMock($"datasource_{Guid.NewGuid():N}", "CommandA", "CommandB", "UnknownCommand");

        await RunHostedService(logger, TimeSpan.FromMilliseconds(50),
            Source(storage, "CommandA"),
            Source(storage, "CommandB"));

        storage.Verify(x => x.GetSummary(It.IsAny<CancellationToken>()), Times.AtLeast(2));
        Assert.That(logger.Messages, Has.Count.EqualTo(1));
        Assert.That(logger.Messages.Single(), Does.StartWith("Warning:"));
        Assert.That(logger.Messages.Single(), Does.Contain("UnknownCommand"));
        Assert.That(logger.Messages.Single(), Does.Not.Contain("CommandA"));
    }

    [Test]
    public async Task ShouldLogCommandTypeWithoutHandlersAgainWhenItAppearsAgain()
    {
        var logger = new ListLogger();
        var storage = StorageMock($"datasource_{Guid.NewGuid():N}");
        var summaries = new Queue<string[]>([["UnknownCommand"], [], ["UnknownCommand"]]);
        storage
            .Setup(x => x.GetSummary(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (summaries.Count > 1 ? summaries.Dequeue() : summaries.Peek())
                .Select(t => new LocalQueueStorageSummary { CommandType = t, Count = 1, MaxTryCount = 1 })
                .ToArray());

        await RunHostedService(logger, TimeSpan.FromMilliseconds(50), Source(storage, "CommandA"));

        Assert.That(logger.Messages, Has.Count.EqualTo(2));
        Assert.That(logger.Messages, Has.All.Contain("UnknownCommand"));
    }

    [Test]
    public async Task ShouldLogCommandTypesWithoutHandlersOnceForStorageCreatedPerScope()
    {
        var logger = new ListLogger();
        var dataSourceName = $"datasource_{Guid.NewGuid():N}";
        var createdStorages = 0;
        var source = new StorageSummarySource(_ =>
        {
            Interlocked.Increment(ref createdStorages);
            return StorageMock(dataSourceName, "UnknownCommand").Object;
        }, ["CommandA"]);

        await RunHostedService(logger, TimeSpan.FromMilliseconds(50), source);

        Assert.That(createdStorages, Is.GreaterThanOrEqualTo(2));
        Assert.That(logger.Messages, Has.Count.EqualTo(1));
    }

    private static Task RunHostedService(ILogger<StorageSummaryCollector> logger,
        params StorageSummarySource[] sources)
    {
        return RunHostedService(logger, null, sources);
    }

    private static async Task RunHostedService(ILogger<StorageSummaryCollector> logger, TimeSpan? interval,
        params StorageSummarySource[] sources)
    {
        var collector = new StorageSummaryCollector(sources, new ServiceScopeFactoryFake(), logger, interval);
        var hostedService = new StorageSummaryHostedService(collector);
        await hostedService.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await hostedService.StopAsync(CancellationToken.None);
    }

    private static StorageSummarySource Source(Mock<ILocalCommandsQueueWorkerStorage> storage,
        params string[] commandTypes)
    {
        return new StorageSummarySource(_ => storage.Object, commandTypes);
    }

    private static StorageSummarySource FailingSource(params string[] commandTypes)
    {
        return new StorageSummarySource(_ => throw new InvalidOperationException("Storage is not ready"), commandTypes);
    }

    private static Mock<ILocalCommandsQueueWorkerStorage> StorageMock(string dataSourceName,
        params string[] storedCommandTypes)
    {
        var storage = new Mock<ILocalCommandsQueueWorkerStorage>();
        storage.SetupGet(x => x.DataSourceName).Returns(dataSourceName);
        storage
            .Setup(x => x.GetSummary(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedCommandTypes
                .Select(t => new LocalQueueStorageSummary { CommandType = t, Count = 1, MaxTryCount = 1 })
                .ToArray());
        return storage;
    }


    private class ListLogger : ILogger<StorageSummaryCollector>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyCollection<string> Messages
        {
            get
            {
                lock (_messages) return _messages.ToArray();
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            lock (_messages) _messages.Add($"{logLevel}: {formatter(state, exception)}");
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
