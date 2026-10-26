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
    private readonly Mock<ILocalCommandsQueueWorkerStorage> _storage;
    private readonly TestCtsHandler _handler;
    private readonly ServiceScopeFactoryFake _scopeFactory;

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

        public CommandRecord ToCommandRecord(int tryCount)
        {
            return new CommandRecord
            {
                Id = Id,
                Data = JsonSerializer.Serialize(this),
                CommandType = typeof(TestCtsCommand).FullName!,
                TryCount = tryCount,
                CreatedAtUtc = DateTime.UtcNow,
                LockedTillUtc = DateTime.UtcNow.AddMinutes(5)
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
        _storage = new Mock<ILocalCommandsQueueWorkerStorage>();

        _scopeFactory = new ServiceScopeFactoryFake();
        _scopeFactory.SetService<ICommandHandler<TestCtsCommand>>(_handler);
        _scopeFactory.SetService(_storage.Object);
    }

    private RawCommandHandler<TestCtsCommand> Sut(Action<CommandProcessingOptions>? configure = null)
    {
        var options = new CommandProcessingOptions()
            .WithBackoffIntervalFor<TestCtsCommand>(TimeSpan.FromMilliseconds(50));

        configure?.Invoke(options);

        return new RawCommandHandler<TestCtsCommand>(
            options,
            sp => _storage.Object,
            (_, command, ct) => _handler.Handle(command, ct),
            _scopeFactory,
            new JsonCommandSerializer(new JsonSerializerOptions()),
            new NullLoggerFactory()
                .CreateLogger<RawCommandHandler<TestCtsCommand>>());
    }

    [Test]
    public async Task ShouldProcessCommand()
    {
        var command = new TestCtsCommand();

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.Processed.Count, Is.EqualTo(1));
        Assert.That(_handler.Processed.Single().Id, Is.EqualTo(command.Id));
    }

    [Test]
    public async Task ShouldNotProcess_WhenLockedTillUtcIsOver()
    {
        var toSkip = new TestCtsCommand();
        
        var skipContext = toSkip.ToCommandRecord(DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow.AddMinutes(-5));

        await Sut().Handle(skipContext, CancellationToken.None);

        Assert.That(_handler.Processed, Has.Count.EqualTo(0));
        AssertDeleteCalled(skipContext.Id, Times.Never);
        AssertMakeInvisibleForNeverCalled(skipContext.Id);
    }

    [Test]
    public async Task ShouldDeleteProcessedCommands()
    {
        var command = new TestCtsCommand();

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        AssertDeleteCalled(command.Id, Times.Once);
        AssertMakeInvisibleForNeverCalled(command.Id);
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

        await Sut().Handle(failingCommand.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.StartedProcessing[failingCommand.Id], Is.EqualTo(3));
        AssertDeleteCalled(failingCommand.Id, Times.Never);
    }

    [Test]
    public async Task ShouldMakeCommandInvisibleAfterFailure_IfRetryAfterFailureOptionIsSpecified()
    {
        var failingCommand = new TestCtsCommand();
        var failingCommandRecord = failingCommand.ToCommandRecord();
        var timeout = TimeSpan.FromSeconds(123);

        _handler.HandleCallback = c =>
        {
            if (c.Id == failingCommandRecord.Id)
            {
                throw new InvalidOperationException($"Throw test exception for command {c.Id}");
            }
            return Task.CompletedTask;
        };

        var sut = Sut(c =>
        {
            c.WithRetryAfterFailure<TestCtsCommand>(timeout);
        });

        await sut.Handle(failingCommand.ToCommandRecord(), CancellationToken.None);

        AssertDeleteCalled(failingCommand.Id, Times.Never);
        AssertMakeInvisibleForCalledOnce(failingCommand.Id, timeout);
    }
    
    [Test]
    public async Task ShouldNotMakeCommandInvisibleAfterFailure_WithoutRetryAfterFailureOption()
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

        var sut = Sut(c =>
        {
            c.WithoutRetryAfterFailure<TestCtsCommand>();
        });

        await sut.Handle(failingCommand.ToCommandRecord(), CancellationToken.None);

        AssertDeleteCalled(failingCommand.Id, Times.Never);
        AssertMakeInvisibleForNeverCalled(failingCommand.Id);
    }

    private class AttemptAwareRetryPolicy : ICommandRetryPolicy
    {
        public int MaxRetryCount => 1;
        public TimeSpan BackoffFor(int retry) => TimeSpan.Zero;
        public TimeSpan? RedeliverAfter(int attempt) => TimeSpan.FromSeconds(attempt * 10);
    }

    [Test]
    public async Task ShouldMakeCommandInvisibleForTheAttemptDependentDelay_WhenPolicyComputesIt()
    {
        var failingCommand = new TestCtsCommand();

        _handler.HandleCallback = c => throw new InvalidOperationException($"Throw test exception for command {c.Id}");

        var sut = Sut(c => c.RetryOptions[typeof(TestCtsCommand).FullName!] = new AttemptAwareRetryPolicy());

        await sut.Handle(failingCommand.ToCommandRecord(tryCount: 4), CancellationToken.None);

        AssertDeleteCalled(failingCommand.Id, Times.Never);
        AssertMakeInvisibleForCalledOnce(failingCommand.Id, TimeSpan.FromSeconds(40));
    }

    private class BackoffRecordingRetryPolicy : ICommandRetryPolicy
    {
        public BackoffRecordingRetryPolicy(int maxRetryCount = 3) => MaxRetryCount = maxRetryCount;

        public List<int> Requested { get; } = new();

        public int MaxRetryCount { get; }

        public TimeSpan BackoffFor(int retry)
        {
            Requested.Add(retry);
            return TimeSpan.Zero;
        }

        public TimeSpan? RedeliverAfter(int attempt) => TimeSpan.FromSeconds(1);
    }

    [Test]
    public async Task ShouldNotBackoffAfterTheLastTry()
    {
        var failingCommand = new TestCtsCommand();
        var retryPolicy = new BackoffRecordingRetryPolicy();

        _handler.HandleCallback = c => throw new InvalidOperationException($"Throw test exception for command {c.Id}");

        var sut = Sut(c => c.RetryOptions[typeof(TestCtsCommand).FullName!] = retryPolicy);

        await sut.Handle(failingCommand.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.StartedProcessing[failingCommand.Id], Is.EqualTo(3));
        Assert.That(retryPolicy.Requested, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task ShouldStopRetrying_WhenTrySucceeds()
    {
        var command = new TestCtsCommand();
        var retryPolicy = new BackoffRecordingRetryPolicy();

        _handler.HandleCallback = c =>
        {
            if (_handler.StartedProcessing[c.Id] == 1)
            {
                throw new InvalidOperationException($"Throw test exception for command {c.Id}");
            }
            return Task.CompletedTask;
        };

        var sut = Sut(c => c.RetryOptions[typeof(TestCtsCommand).FullName!] = retryPolicy);

        await sut.Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.StartedProcessing[command.Id], Is.EqualTo(2));
        Assert.That(retryPolicy.Requested, Is.EqualTo(new[] { 1 }));
        AssertDeleteCalled(command.Id, Times.Once);
        AssertMakeInvisibleForNeverCalled(command.Id);
    }

    [Test]
    public async Task ShouldNotTryAtAll_WhenMaxRetryCountIsZero()
    {
        var command = new TestCtsCommand();
        var retryPolicy = new BackoffRecordingRetryPolicy(0);

        var sut = Sut(c => c.RetryOptions[typeof(TestCtsCommand).FullName!] = retryPolicy);

        await sut.Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.StartedProcessing, Is.Empty);
        Assert.That(retryPolicy.Requested, Is.Empty);
        AssertDeleteCalled(command.Id, Times.Never);
        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromSeconds(1));
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

        await Sut().Handle(context, cts.Token);

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

        await Sut().Handle(lockedContext, CancellationToken.None);

        Assert.That(_handler.StartedProcessing[lockedContext.Id], Is.EqualTo(1));
        AssertDeleteCalled(lockedContext.Id, Times.Never);
        AssertMakeInvisibleForNeverCalled(lockedContext.Id);
    }

    private void AssertDeleteCalled(Guid commandId, Func<Times> times)
    {
        _storage.Verify<Task>(
            s => s.Delete(commandId, It.IsAny<CancellationToken>()), times);
    }

    private void AssertMakeInvisibleForNeverCalled(Guid commandId)
    {
        _storage.Verify<Task>(
            s => s.MakeInvisibleFor(commandId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AssertMakeInvisibleForCalledOnce(Guid commandId, TimeSpan timeout)
    {
        _storage.Verify<Task>(
            s => s.MakeInvisibleFor(commandId, timeout, It.IsAny<CancellationToken>()), Times.Once);
    }
}