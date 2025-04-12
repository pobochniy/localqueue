using System.Data;
using System.Text.Json;
using LocalQueue.Queue;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using Moq;

namespace LocalQueue.Tests.Queue;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandsQueueTests
{
    private readonly Mock<IDbConnection> _connection;
    private readonly Mock<IDbTransaction> _transaction;
    private readonly Mock<ICommandsStorage> _storage;
    private readonly CommandsQueue _sut;

    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private record TestCommand
    {
        public Guid Id { get; set; }
        public string StringProperty { get; set; } = null!;
        public DateTime DateTimeProperty { get; set; }
        public int IntProperty { get; set; }
        public double DoubleProperty { get; set; }
        public PrivateData Data { get; set; } = null!;
        public PrivateData? NullableData { get; set; }

        public class PrivateData
        {
            public Guid Id { get; set; }
            public string StringProperty { get; set; } = null!;
            public DateTime DateTimeProperty { get; set; }
            public int IntProperty { get; set; }
            public double DoubleProperty { get; set; }
        }

        public static TestCommand Create()
        {
            return new TestCommand
            {
                Id = Guid.NewGuid(),
                StringProperty = $"{nameof(StringProperty)}_{Random.Shared.Next()}",
                DateTimeProperty = DateTime.UtcNow,
                IntProperty = Random.Shared.Next(),
                DoubleProperty = Random.Shared.NextDouble(),
                Data = new PrivateData
                {
                    Id = Guid.NewGuid(),
                    StringProperty = $"{nameof(PrivateData.StringProperty)}_{Random.Shared.Next()}",
                    DateTimeProperty = DateTime.UtcNow,
                    IntProperty = Random.Shared.Next(),
                    DoubleProperty = Random.Shared.NextDouble(),
                },
                NullableData = null
            };
        }
    }

    public CommandsQueueTests()
    {
        _connection = new();
        _transaction = new();
        _storage = new();
        _sut = new CommandsQueue(_storage.Object, new JsonCommandSerializer(new JsonSerializerOptions()));
    }

    [Test]
    public async Task ShouldNotSaveEmptyCollection()
    {
        var commands = Array.Empty<TestCommand>();

        await _sut.Enqueue(_connection.Object, _transaction.Object, commands, CancellationToken.None);

        _storage.Verify<Task>(
            s => s.Create(
                It.IsAny<IDbConnection>(),
                It.IsAny<IDbTransaction>(),
                It.IsAny<IEnumerable<CommandRecord>>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ShouldCreateRecordWithValidProperties()
    {
        var command = TestCommand.Create();
        CommandRecord[]? actualRecords = null;

        _storage
            .Setup(s => s.Create(
                It.IsAny<IDbConnection>(),
                It.IsAny<IDbTransaction>(),
                It.IsAny<IEnumerable<CommandRecord>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IDbConnection, IDbTransaction, IEnumerable<CommandRecord>, CancellationToken>((_, _, r, _) =>
            {
                actualRecords = r.ToArray();
            });

        await _sut.Enqueue(_connection.Object, _transaction.Object, new [] {command}, CancellationToken.None);

        Assert.That(actualRecords, Has.Length.EqualTo(1));
        var actualRecord = actualRecords![0];

        Assert.That(actualRecord.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(actualRecord.Data, Is.EqualTo(JsonSerializer.Serialize(command)));
        Assert.That(actualRecord.CommandType, Is.EqualTo(typeof(TestCommand).FullName!));
        Assert.That(actualRecord.CreatedAtUtc, Is.EqualTo(DateTime.UtcNow).Within(5).Seconds);
        Assert.That(actualRecord.LockedTillUtc, Is.Null);
        Assert.That(actualRecord.TryCount, Is.EqualTo(0));
    }
}