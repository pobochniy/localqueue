using System.Collections.Concurrent;
using System.Text.Json;
using LocalQueue.Processing;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using LocalQueue.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LocalQueue.Tests.Processing;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandProcessorTests
{
    private readonly FetchCommandChannel _fetchChannel;
    private readonly CommandProcessor _sut;
    private readonly TestHandler _handler;

    private class TestCommand
    {
        public Guid Id { get; set; } = Guid.NewGuid();

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
                CommandType = typeof(TestCommand).FullName!,
                TryCount = 0,
                CreatedAtUtc = createdAtUtc,
                LockedTillUtc = lockedTill
            };
        }
    }

    private class TestHandler : ICommandHandler<TestCommand>
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _handledCommands = new();

        public Task Handle(TestCommand command, CancellationToken ct)
        {
            var taskCompletionSource = _handledCommands.GetOrAdd(command.Id,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            taskCompletionSource.SetResult();
            return Task.CompletedTask;
        }

        public Task WaitCommandHandled(TestCommand command)
        {
            var taskCompletionSource = _handledCommands.GetOrAdd(command.Id,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            return taskCompletionSource.Task;
        }
    }

    public CommandProcessorTests()
    {
        _handler = new TestHandler();
        var storage = new Mock<ICommandsStorage>();

        var scopeFactory = new ServiceScopeFactoryFake();
        scopeFactory.SetService<ICommandHandler<TestCommand>>(_handler);
        scopeFactory.SetService(storage.Object);

        _fetchChannel = new(10);

        var rawHandler = new RawCommandHandler<TestCommand>(
            new CommandProcessingOptions(),
            scopeFactory,
            new JsonCommandSerializer(new JsonSerializerOptions()),
            Logger<RawCommandHandler<TestCommand>>());

        _sut = new CommandProcessor(
            _fetchChannel,
            new[] { rawHandler },
            Logger<CommandProcessor>());
    }

    private static ILogger<T> Logger<T>()
    {
        return new NullLoggerFactory().CreateLogger<T>();
    }

    [Test]
    public void WhenCreatedShouldThrowException_IfHandlersContainsDuplicates()
    {
        var factoryMock = new Mock<IServiceScopeFactory>();
        var options = new CommandProcessingOptions();
        var handlers = new IRawCommandHandler[]
        {
            GenerateHandler<TestCommand>(),
            GenerateHandler<TestCommand>(),
            GenerateHandler<object>(),
            GenerateHandler<object>(),
            GenerateHandler<string>(),
        };

        var expectedDuplicates = new[]
        {
            typeof(TestCommand).FullName!,
            typeof(object).FullName!
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CommandProcessor(_fetchChannel, handlers, Logger<CommandProcessor>()));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message,
            Is.EqualTo($"Handlers must not contain duplicates. Duplicates: {string.Join(",", expectedDuplicates)}"));
        return;

        RawCommandHandler<TCommand> GenerateHandler<TCommand>()
        {
            return new RawCommandHandler<TCommand>(options, factoryMock.Object, 
                new JsonCommandSerializer(new JsonSerializerOptions()), Logger<RawCommandHandler<TCommand>>());
        }
    }

    [Test, Timeout(3_000)]
    public async Task ShouldProcessCommand()
    {
        var command = new TestCommand();
        var record = command.ToCommandRecord();
        await _fetchChannel.Writer.WriteAsync(record, CancellationToken.None);

        _ = _sut.ExecuteAsync(CancellationToken.None);

        await _handler.WaitCommandHandled(command);
        Assert.Pass();
    }

    [Test, Timeout(3_000)]
    public async Task ShouldNotProcessCommand_IfHandlerNotFound()
    {
        var sickCommand = new
        {
            Id = Guid.NewGuid()
        };

        var sickRecord = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = sickCommand.GetType().FullName!,
            TryCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            LockedTillUtc = DateTime.UtcNow.AddMinutes(5),
            Data = JsonSerializer.Serialize(sickCommand)
        };

        var healthyCommand = new TestCommand();
        var healthyRecord = healthyCommand.ToCommandRecord();

        await _fetchChannel.Writer.WriteAsync(sickRecord, CancellationToken.None);
        await _fetchChannel.Writer.WriteAsync(healthyRecord, CancellationToken.None);

        _ = _sut.ExecuteAsync(CancellationToken.None);

        await _handler.WaitCommandHandled(healthyCommand);
        Assert.Pass();
    }
}