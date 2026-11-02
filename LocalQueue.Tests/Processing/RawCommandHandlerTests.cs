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
        // The commands are held by the worker unless a test takes them away
        _storage.Setup(s => s.Delete(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storage.Setup(s => s.MakeInvisibleFor(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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

    [Test]
    public async Task ShouldExtendLock_WhenHandlerExtendsLease()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        var duration = TimeSpan.FromMinutes(10);

        _handler.HandleCallback = _ => lease.Extend(duration, CancellationToken.None);

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        // Locked for a half more, so the next extensions skip the write for that half
        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(15));
        AssertDeleteCalled(command.Id, Times.Once);
    }

    [Test]
    public async Task ShouldNotShortenLock_WhenLeaseIsExtendedForLessThanLeft()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);

        // The prefetch lock ends in 5 minutes
        _handler.HandleCallback = _ => lease.Extend(TimeSpan.FromMinutes(1), CancellationToken.None);

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        AssertMakeInvisibleForNeverCalled(command.Id);
        AssertDeleteCalled(command.Id, Times.Once);
    }

    [Test]
    public async Task ShouldExtendLockOfOwnCommand_WhenCommandsAreProcessedInParallel()
    {
        var first = new TestCtsCommand();
        var second = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        var bothStarted = new Barrier(2);

        _handler.HandleCallback = async c =>
        {
            // Both commands are inside the handler before any of them extends the lock
            await Task.Run(() => bothStarted.SignalAndWait(TimeSpan.FromSeconds(5)));
            await lease.Extend(c.Id == first.Id ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(20),
                CancellationToken.None);
        };

        var sut = Sut();
        await Task.WhenAll(
            Task.Run(() => sut.Handle(first.ToCommandRecord(), CancellationToken.None)),
            Task.Run(() => sut.Handle(second.ToCommandRecord(), CancellationToken.None)));

        AssertMakeInvisibleForCalledOnce(first.Id, TimeSpan.FromMinutes(15));
        AssertMakeInvisibleForCalledOnce(second.Id, TimeSpan.FromMinutes(30));
    }

    [Test]
    public async Task ShouldRetry_WhenPrefetchLockIsOverButLeaseIsExtended()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        // The prefetch lock ends before the second try
        var record = command.ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMilliseconds(500));

        var tries = 0;
        _handler.HandleCallback = async _ =>
        {
            if (++tries > 1) return;

            await lease.Extend(TimeSpan.FromMinutes(5), CancellationToken.None);
            throw new InvalidOperationException("The first try fails after extending the lease");
        };

        await Sut(o => o.WithBackoffIntervalFor<TestCtsCommand>(TimeSpan.FromSeconds(1)))
            .Handle(record, CancellationToken.None);

        Assert.That(_handler.StartedProcessing[command.Id], Is.EqualTo(2));
        Assert.That(_handler.Processed, Has.Count.EqualTo(1));
        AssertDeleteCalled(command.Id, Times.Once);
    }

    [Test]
    public async Task ShouldNotRetry_WhenPrefetchLockIsOverAndLeaseIsNotExtended()
    {
        var command = new TestCtsCommand();
        var record = command.ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMilliseconds(500));

        _handler.HandleCallback = _ => throw new InvalidOperationException("Fails while the lock is still taken");

        await Sut(o => o.WithBackoffIntervalFor<TestCtsCommand>(TimeSpan.FromSeconds(1)))
            .Handle(record, CancellationToken.None);

        Assert.That(_handler.StartedProcessing[command.Id], Is.EqualTo(1));
        AssertDeleteCalled(command.Id, Times.Never);
    }

    [Test]
    public async Task ShouldStopWithoutRetries_WhenCommandIsTakenByAnotherInstance()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        TakenByAnotherInstance(command.Id);

        Exception? thrown = null;
        _handler.HandleCallback = async _ =>
        {
            try
            {
                await lease.Extend(TimeSpan.FromMinutes(10), CancellationToken.None);
            }
            catch (Exception e)
            {
                thrown = e;
                throw;
            }
        };

        await Sut(o => o.WithRetryAfterFailure<TestCtsCommand>(TimeSpan.FromMinutes(1)))
            .Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(thrown, Is.TypeOf<CommandLeaseExpiredException>()
            .With.Property(nameof(CommandLeaseExpiredException.CommandId)).EqualTo(command.Id));
        Assert.That(_handler.StartedProcessing[command.Id], Is.EqualTo(1));
        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(15));
        AssertDeleteCalled(command.Id, Times.Never);
    }

    [Test]
    public async Task ShouldStopWithoutRetries_WhenHandlerWrapsLeaseExpiredException()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        TakenByAnotherInstance(command.Id);

        _handler.HandleCallback = async _ =>
        {
            try
            {
                await Task.WhenAll(lease.Extend(TimeSpan.FromMinutes(10), CancellationToken.None));
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("The step has failed", new AggregateException(e));
            }
        };

        await Sut(o => o.WithRetryAfterFailure<TestCtsCommand>(TimeSpan.FromMinutes(1)))
            .Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(_handler.StartedProcessing[command.Id], Is.EqualTo(1));
        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(15));
        AssertDeleteCalled(command.Id, Times.Never);
    }

    [Test]
    public async Task ShouldExtendLock_WhenLockIsOverButCommandIsNotTakenByAnotherInstance()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        var record = command.ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMilliseconds(100));

        _handler.HandleCallback = async _ =>
        {
            await Task.Delay(300);
            await lease.Extend(TimeSpan.FromMinutes(5), CancellationToken.None);
        };

        await Sut().Handle(record, CancellationToken.None);

        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(7.5));
        AssertDeleteCalled(command.Id, Times.Once);
    }

    [Test]
    public async Task ShouldNotShortenLock_WhenLeaseIsExtendedInParallel()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        var longerWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage.Setup(s => s.MakeInvisibleFor(command.Id, It.IsAny<int>(), TimeSpan.FromMinutes(30),
                It.IsAny<CancellationToken>()))
            .Returns(longerWrite.Task);

        _handler.HandleCallback = async _ =>
        {
            var longer = lease.Extend(TimeSpan.FromMinutes(20), CancellationToken.None);
            var shorter = lease.Extend(TimeSpan.FromMinutes(10), CancellationToken.None);
            longerWrite.SetResult(true);
            await Task.WhenAll(longer, shorter);
        };

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(30));
        AssertMakeInvisibleForNeverCalledFor(command.Id, TimeSpan.FromMinutes(15));
    }

    [Test]
    public async Task ShouldWriteLockOncePerHalfOfDuration_WhenLeaseIsExtendedBeforeEveryStep()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);

        _handler.HandleCallback = async _ =>
        {
            for (var step = 0; step < 100; step++)
            {
                await lease.Extend(TimeSpan.FromMinutes(10), CancellationToken.None);
            }
        };

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(15));
    }

    [Test]
    public async Task ShouldExtendLock_WhenClockOfStorageIsAhead()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        // The storage says 5 minutes by its clock, the instance has counted 100 ms from before the prefetch
        var record = command.ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        record.LocalLockedTillMs = Environment.TickCount64 + 100;

        _handler.HandleCallback = _ => lease.Extend(TimeSpan.FromMinutes(1), CancellationToken.None);

        await Sut().Handle(record, CancellationToken.None);

        AssertMakeInvisibleForCalledOnce(command.Id, TimeSpan.FromMinutes(1.5));
    }

    [Test]
    public async Task ShouldNotRetry_WhenLockIsOverByInstanceClockButNotByStorageClock()
    {
        var command = new TestCtsCommand();
        var record = command.ToCommandRecord(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        record.LocalLockedTillMs = Environment.TickCount64 + 100;

        _handler.HandleCallback = async _ =>
        {
            await Task.Delay(300);
            throw new InvalidOperationException("Fails after the lock is over");
        };

        await Sut().Handle(record, CancellationToken.None);

        Assert.That(_handler.StartedProcessing[command.Id], Is.EqualTo(1));
    }

    [Test]
    public async Task ShouldNotLockCommand_WhenLeaseIsExtendedAfterProcessing()
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? leftRunning = null;

        _handler.HandleCallback = _ =>
        {
            leftRunning = Task.Run(async () =>
            {
                await processed.Task;
                await lease.Extend(TimeSpan.FromMinutes(5), CancellationToken.None);
            });
            return Task.CompletedTask;
        };

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);
        processed.SetResult();

        await Assert.ThrowsAsync<CommandLeaseExpiredException>(() => leftRunning!);
        AssertMakeInvisibleForNeverCalled(command.Id);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(14 * 24 * 60 + 1)]
    public async Task ShouldNotChangeLock_WhenLeaseIsExtendedForDurationOutOfRange(int minutes)
    {
        var command = new TestCtsCommand();
        ICommandLease lease = new CommandLease(NullLogger<CommandLease>.Instance);
        Exception? thrown = null;

        _handler.HandleCallback = async _ =>
        {
            try
            {
                await lease.Extend(TimeSpan.FromMinutes(minutes), CancellationToken.None);
            }
            catch (Exception e)
            {
                thrown = e;
            }
        };

        await Sut().Handle(command.ToCommandRecord(), CancellationToken.None);

        Assert.That(thrown, Is.TypeOf<ArgumentOutOfRangeException>());
        AssertMakeInvisibleForNeverCalled(command.Id);
    }

    [Test]
    public async Task ShouldNotHideCommandAfterFailure_WhenCommandIsTakenByAnotherInstance()
    {
        var command = new TestCtsCommand();
        var record = command.ToCommandRecord(tryCount: 3);
        _handler.HandleCallback = _ => throw new InvalidOperationException("Fails every try");

        await Sut(o => o.WithRetryAfterFailure<TestCtsCommand>(TimeSpan.FromMinutes(1)))
            .Handle(record, CancellationToken.None);

        // The storage gets the try count of the prefetch and hides the command only while it is the same
        _storage.Verify(s => s.MakeInvisibleFor(command.Id, 3, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ShouldDeleteCommandOfOwnPrefetch()
    {
        var command = new TestCtsCommand();

        await Sut().Handle(command.ToCommandRecord(tryCount: 2), CancellationToken.None);

        _storage.Verify(s => s.Delete(command.Id, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LeaseOutsideOfWorker_LogsWarningAndDoesNothing()
    {
        var logger = new RecordingLogger<CommandLease>();
        ICommandLease lease = new CommandLease(logger);

        await lease.Extend(TimeSpan.FromMinutes(5), CancellationToken.None);

        _storage.VerifyNoOtherCalls();
        Assert.That(logger.Levels, Is.EqualTo(new[] { LogLevel.Warning }));
    }

    private void TakenByAnotherInstance(Guid commandId)
    {
        _storage.Setup(s => s.MakeInvisibleFor(commandId, It.IsAny<int>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _storage.Setup(s => s.Delete(commandId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private void AssertMakeInvisibleForNeverCalledFor(Guid commandId, TimeSpan timeout)
    {
        _storage.Verify<Task<bool>>(
            s => s.MakeInvisibleFor(commandId, It.IsAny<int>(), timeout, It.IsAny<CancellationToken>()), Times.Never);
    }

    private class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private void AssertDeleteCalled(Guid commandId, Func<Times> times)
    {
        _storage.Verify<Task<bool>>(
            s => s.Delete(commandId, It.IsAny<int>(), It.IsAny<CancellationToken>()), times);
    }

    private void AssertMakeInvisibleForNeverCalled(Guid commandId)
    {
        _storage.Verify<Task<bool>>(
            s => s.MakeInvisibleFor(commandId, It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AssertMakeInvisibleForCalledOnce(Guid commandId, TimeSpan timeout)
    {
        _storage.Verify<Task<bool>>(
            s => s.MakeInvisibleFor(commandId, It.IsAny<int>(), timeout, It.IsAny<CancellationToken>()), Times.Once);
    }
}