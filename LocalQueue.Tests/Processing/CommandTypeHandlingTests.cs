using System.Diagnostics.Metrics;
using System.Text.Json;
using LocalQueue.Diagnostics;
using LocalQueue.Processing;
using LocalQueue.RetryPolicy;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using LocalQueue.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LocalQueue.Tests.Processing;

/// <summary>
/// The names of the command type and the limit of attempts of its retry policy.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandTypeHandlingTests
{
    private readonly Mock<ILocalCommandsQueueWorkerStorage> _storage = new();
    private readonly List<int> _handled = new();
    private Func<Task> _handle = () => Task.CompletedTask;

    [CommandType("mail", "Old.MailCommand")]
    private class MailCommand
    {
        public int Number { get; init; }
    }

    public CommandTypeHandlingTests()
    {
        _storage.SetupGet(s => s.DataSourceName).Returns("db.public.queue");
        _storage.Setup(s => s.Delete(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storage.Setup(s => s.MakeInvisibleFor(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RawCommandHandler<MailCommand> Sut(ICommandRetryPolicy? retryPolicy = null)
    {
        var options = new CommandProcessingOptions();
        if (retryPolicy != null)
        {
            options.RetryOptions[CommandTypes.NameOf<MailCommand>()] = retryPolicy;
        }

        var scopeFactory = new ServiceScopeFactoryFake();
        scopeFactory.SetService(_storage.Object);
        return new RawCommandHandler<MailCommand>(
            options,
            _ => _storage.Object,
            async (_, command, _) =>
            {
                _handled.Add(command.Number);
                await _handle();
            },
            scopeFactory,
            new JsonCommandSerializer(new JsonSerializerOptions()),
            NullLogger<RawCommandHandler<MailCommand>>.Instance);
    }

    private static CommandRecord Record(string commandType, int tryCount = 1)
    {
        return new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Data = JsonSerializer.Serialize(new MailCommand { Number = tryCount }),
            CreatedAtUtc = DateTime.UtcNow,
            LockedTillUtc = DateTime.UtcNow.AddMinutes(5),
            TryCount = tryCount,
            LocalLockedTillMs = Environment.TickCount64 + 60_000
        };
    }

    [TestCase("mail")]
    [TestCase("Old.MailCommand")]
    [TestCase("LocalQueue.Tests.Processing.CommandTypeHandlingTests+MailCommand")]
    public void ShouldHandleCommandsOfEveryName(string commandType)
    {
        Assert.That(Sut().CommandType, Is.EqualTo("mail"));
        Assert.That(Sut().CanHandle(Record(commandType)), Is.True);
    }

    [Test]
    public void ShouldNotHandleCommandsOfOtherTypes()
    {
        Assert.That(Sut().CanHandle(Record("mail-2")), Is.False);
    }

    [Test]
    public async Task ShouldProcessCommandOfPreviousName()
    {
        var record = Record("Old.MailCommand");

        await Sut().Handle(record, CancellationToken.None);

        Assert.That(_handled, Has.Count.EqualTo(1));
        _storage.Verify(s => s.Delete(record.Id, record.TryCount, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ShouldGiveUp_WhenTheLastAttemptFails()
    {
        _handle = () => throw new InvalidOperationException("fails");
        var record = Record("mail", tryCount: 3);
        using var givenUp = new GivenUpListener();

        await Sut(Policy(maxAttempts: 3)).Handle(record, CancellationToken.None);

        Assert.That(_handled, Has.Count.EqualTo(1));
        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 3, ICommandRetryPolicy.GivenUpLock, It.IsAny<CancellationToken>()),
            Times.Once);
        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 3, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(givenUp.Count("mail"), Is.EqualTo(1));
    }

    [Test]
    public async Task ShouldRedeliver_WhenAttemptsAreLeft()
    {
        _handle = () => throw new InvalidOperationException("fails");
        var record = Record("mail", tryCount: 2);

        await Sut(Policy(maxAttempts: 3)).Handle(record, CancellationToken.None);

        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 2, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()),
            Times.Once);
        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 2, ICommandRetryPolicy.GivenUpLock, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ShouldGiveUpWithoutHandling_WhenPrefetchedBeyondTheLimit()
    {
        // The 3rd delivery didn't finish: the process was killed in the middle of the command
        var record = Record("Old.MailCommand", tryCount: 4);
        using var givenUp = new GivenUpListener();

        await Sut(Policy(maxAttempts: 3)).Handle(record, CancellationToken.None);

        Assert.That(_handled, Is.Empty);
        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 4, ICommandRetryPolicy.GivenUpLock, It.IsAny<CancellationToken>()),
            Times.Once);
        _storage.Verify(s => s.Delete(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(givenUp.Count("mail"), Is.EqualTo(1));
    }

    [Test]
    public async Task ShouldProcessTheLastAttempt()
    {
        var record = Record("mail", tryCount: 3);

        await Sut(Policy(maxAttempts: 3)).Handle(record, CancellationToken.None);

        Assert.That(_handled, Has.Count.EqualTo(1));
        _storage.Verify(s => s.Delete(record.Id, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ShouldNotGiveUp_WithoutTheLimit()
    {
        _handle = () => throw new InvalidOperationException("fails");
        var record = Record("mail", tryCount: 1000);

        await Sut(Policy(maxAttempts: null)).Handle(record, CancellationToken.None);

        Assert.That(_handled, Has.Count.EqualTo(1));
        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 1000, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ShouldNotGiveUp_WhenCustomPolicyHasNoLimit()
    {
        _handle = () => throw new InvalidOperationException("fails");
        var record = Record("mail", tryCount: 1000);

        await Sut(new PolicyWithoutLimit()).Handle(record, CancellationToken.None);

        _storage.Verify(s => s.MakeInvisibleFor(record.Id, 1000, ICommandRetryPolicy.GivenUpLock, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static RetryPolicyOptions Policy(int? maxAttempts)
    {
        return new RetryPolicyOptions
        {
            MaxRetryCount = 1,
            RetryAfterFailure = TimeSpan.FromMinutes(1),
            MaxAttempts = maxAttempts
        };
    }

    // The policy written before MaxAttempts: the default member of the interface keeps it without a limit
    private class PolicyWithoutLimit : ICommandRetryPolicy
    {
        public int MaxRetryCount => 1;

        public TimeSpan BackoffFor(int retry) => TimeSpan.Zero;

        public TimeSpan? RedeliverAfter(int attempt) => TimeSpan.FromMinutes(1);
    }

    private sealed class GivenUpListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<string, int> _counts = new();

        public GivenUpListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Instruments.MeterName && instrument.Name == "local-command-queue-given-up")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key != "commandType") continue;
                    lock (_counts)
                    {
                        _counts[(string)tag.Value!] = _counts.GetValueOrDefault((string)tag.Value!) + value;
                    }
                }
            });
            _listener.Start();
        }

        public int Count(string commandType)
        {
            lock (_counts) return _counts.GetValueOrDefault(commandType);
        }

        public void Dispose() => _listener.Dispose();
    }
}
