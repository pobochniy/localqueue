using LocalQueue.Processing;
using LocalQueue.Storage;
using LocalQueue.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LocalQueue.Tests.Processing;

[TestFixture]
public class CommandFetcherTests
{
    [Test]
    public async Task ShouldCountLockByInstanceClockFromBeforePrefetch()
    {
        var timeout = TimeSpan.FromMinutes(1);
        var storage = new Mock<ILocalCommandsQueueWorkerStorage>();
        // The clock of the storage is far ahead: the instance must not rely on it
        storage.Setup(s => s.Prefetch(It.IsAny<int>(), timeout, It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [new CommandRecord { Id = Guid.NewGuid(), LockedTillUtc = DateTime.UtcNow.AddHours(1) }]);
        var channel = new FetchCommandChannel(1);
        var fetcher = new CommandFetcher(new ServiceScopeFactoryFake(), _ => storage.Object,
            new CommandProcessingOptions { InvisibilityTimeout = timeout, PrefetchCount = 1 }, ["type"], channel,
            NullLogger<CommandFetcher>.Instance);
        using var cts = new CancellationTokenSource();

        var before = Environment.TickCount64;
        var run = fetcher.Run(cts.Token);
        var record = await channel.Reader.ReadAsync(cts.Token);
        var after = Environment.TickCount64;
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // stopped
        }

        Assert.That(record.LocalLockedTillMs,
            Is.InRange(before + (long)timeout.TotalMilliseconds, after + (long)timeout.TotalMilliseconds));
    }
}
