using System.Collections.Concurrent;
using System.Text.Json;
using LocalQueue.Processing;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using LocalQueue.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LocalQueue.Tests.Processing;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class RawCommandHandlerTests
{
    private readonly Mock<ICommandsStorage> _storage;
    private readonly RawCommandHandler<TestCtsCommand> _sut;
    private readonly TestCtsHandler _handler;

    private class TestCtsCommand
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public CommandRecord ToCommandRecord()
        {
            return ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        }

        public CommandRecord ToCommandRecord(DateTime createdAtUtc, DateTime lockedTill)
        {
            return new CommandRecord
            {
                Id = Id,
                Data = JsonSerializer.Serialize(this),
                CommandType = typeof(TestCtsCommand).FullName!,
                TryCount = 0,
                CreatedAtUtc = createdAtUtc,
                LockedTillUtc = lockedTill
            };
        }
    }

    private class TestCtsHandler : ICommandHandler<TestCtsCommand>
    {
        private readonly ConcurrentDictionary<Guid, int> _started = new();
        private readonly ConcurrentDictionary<Guid, TestCtsCommand> _completed = new();
        
        public IDictionary<Guid, int> StartedProcessing => _started;
        public IReadOnlyCollection<TestCtsCommand> Processed => _completed.Values.ToList();

        public Func<TestCtsCommand, Task>? HandleCallback { get; set; }

        public async Task Handle(TestCtsCommand command, CancellationToken ct)
        {
            _started.AddOrUpdate(command.Id, _ => 1, (_, i) => i + 1);

            if (HandleCallback != null)
            {
                await HandleCallback(command);
            }

            _completed.TryAdd(command.Id, command);
        }
    }

    public RawCommandHandlerTests()
    {
        _handler = new TestCtsHandler();
        _storage = new Mock<ICommandsStorage>();

        var scopeFactory = new ServiceScopeFactoryFake();
        scopeFactory.SetService<ICommandHandler<TestCtsCommand>>(_handler);
        scopeFactory.SetService(_storage.Object);

        var logger = new NullLoggerFactory()
            .CreateLogger<RawCommandHandler<TestCtsCommand>>();

        var commandProcessingOptions = new CommandProcessingOptions()
            .WithBackoffIntervalFor<TestCtsCommand>(TimeSpan.FromMilliseconds(50));
        
        _sut = new RawCommandHandler<TestCtsCommand>(
            commandProcessingOptions,
            scopeFactory,
            new JsonCommandSerializer(new JsonSerializerOptions()),
            logger);
    }

    [Test]
    public async Task ShouldProcessCommand()
    {
        var command = new TestCtsCommand();

        await _sut.Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.Processed.Count, Is.EqualTo(1));
        Assert.That(_handler.Processed.Single().Id, Is.EqualTo(command.Id));
    }

    [Test]
    public async Task ShouldNotProcess_WhenLockedTillUtcIsOver()
    {
        var toSkip = new TestCtsCommand();
        
        var skipContext = toSkip.ToCommandRecord(DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow.AddMinutes(-5));

        await _sut.Handle(skipContext, CancellationToken.None);

        Assert.That(_handler.Processed, Has.Count.EqualTo(0));
        AssertDeleteCalled(skipContext.Id, Times.Never);
    }

    [Test]
    public async Task ShouldDeleteProcessedCommands()
    {
        var command = new TestCtsCommand();

        await _sut.Handle(command.ToCommandRecord(), CancellationToken.None);

        AssertDeleteCalled(command.Id, Times.Once);
    }

    [Test]
    public async Task ShouldRetryProcess_IfExceptionOccured()
    {
        var failingCommand = new TestCtsCommand();
        var failingCommandRecord = failingCommand.ToCommandRecord();

        _handler.HandleCallback = c =>
        {
            if (c.Id == failingCommandRecord.Id)
            {
                throw new InvalidOperationException($"Throw test exception for command {c.Id}");
            }
            return Task.CompletedTask;
        };

        await _sut.Handle(failingCommand.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.StartedProcessing[failingCommand.Id], Is.EqualTo(3));
        AssertDeleteCalled(failingCommand.Id, Times.Never);
    }

    [Test]
    public async Task ShouldCancelProcessRetries_IfCancellationRequested()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var command = new TestCtsCommand();
        var context = command.ToCommandRecord();

        _handler.HandleCallback = c =>
        {
            if (_handler.StartedProcessing[context.Id] == 2)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException($"Throw test exception for command {c.Id}");
        };

        await _sut.Handle(context, cts.Token);

        Assert.That(_handler.StartedProcessing[context.Id], Is.EqualTo(2));
    }

    [Test]
    public async Task ShouldNotRetryProcess_IfLockedTillIsOver()
    {
        var lockedContext = new TestCtsCommand().ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMilliseconds(200));

        _handler.HandleCallback = async c =>
        {
            if (c.Id == lockedContext.Id)
            {
                await Task.Delay(200);
                throw new InvalidOperationException($"Throw test exception for command {c.Id}");
            }
        };

        await _sut.Handle(lockedContext, CancellationToken.None);

        Assert.That(_handler.StartedProcessing[lockedContext.Id], Is.EqualTo(1));
        AssertDeleteCalled(lockedContext.Id, Times.Never);
    }

    private void AssertDeleteCalled(Guid commandId, Func<Times> times)
    {
        _storage.Verify<Task>(
            s => s.Delete(commandId, It.IsAny<CancellationToken>()), times);
    }
}