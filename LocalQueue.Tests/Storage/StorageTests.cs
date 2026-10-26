using System.Data;
using LocalQueue.Storage;

namespace LocalQueue.Tests.Storage;

public abstract class StorageTests
{
    protected abstract ILocalCommandsQueueWorkerStorage CreateWorkerStorage();
    protected abstract ILocalCommandsQueueStorage CreateCommandsStorage();

    protected abstract Task ExecuteInTransaction(Func<IDbConnection, IDbTransaction, Task> action,
        CancellationToken ct = default);

    [Test]
    public async Task CanPrefetchCreatedCommands()
    {
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = "type",
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandsStorage = CreateCommandsStorage();
        var workerStorage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands = (await workerStorage.Prefetch(1, TimeSpan.FromMinutes(1), [command.CommandType], CancellationToken.None)).ToArray();

        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));
        var fetchedCommand = fetchedCommands.Single();
        Assert.That(fetchedCommand.Id, Is.EqualTo(command.Id));
        Assert.That(fetchedCommand.CommandType, Is.EqualTo(command.CommandType));
        Assert.That(fetchedCommand.Data, Is.EqualTo(command.Data));
        Assert.That(fetchedCommand.CreatedAtUtc, Is.EqualTo(command.CreatedAtUtc));
        Assert.That(fetchedCommand.LockedTillUtc,
            Is.EqualTo(DateTime.UtcNow + TimeSpan.FromMinutes(1)).Within(10).Seconds);
        Assert.That(fetchedCommand.TryCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CannotPrefetchTheSameCommandTillLocked()
    {
        var commandType = $"CommandType_{nameof(CannotPrefetchTheSameCommandTillLocked)}";
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandsStorage = CreateCommandsStorage();
        var storage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), [command.CommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));

        fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), [command.CommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task CanPrefetchTheSameCommandAfterLockExpired()
    {
        var commandType = $"CommandType_{nameof(CanPrefetchTheSameCommandAfterLockExpired)}";
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandsStorage = CreateCommandsStorage();
        var storage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands =
            await storage.Prefetch(1, TimeSpan.FromMilliseconds(500), [command.CommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));
        await Task.Delay(TimeSpan.FromSeconds(2));

        fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), [command.CommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));
        Assert.That(fetchedCommands.Single().TryCount, Is.EqualTo(2));
    }

    [Test]
    public async Task CannotPrefetchCommandsOfOtherTypes()
    {
        var commandType = $"CommandType_{nameof(CannotPrefetchCommandsOfOtherTypes)}";
        var otherCommandType = $"OtherCommandType_{nameof(CannotPrefetchCommandsOfOtherTypes)}";
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = otherCommandType,
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandsStorage = CreateCommandsStorage();
        var workerStorage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands = await workerStorage.Prefetch(1, TimeSpan.FromMinutes(1), [commandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(0));

        fetchedCommands = await workerStorage.Prefetch(1, TimeSpan.FromMinutes(1), [commandType, otherCommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Single().Id, Is.EqualTo(command.Id));
    }

    [Test]
    public async Task CanPrefetchOldestCommandsOfSeveralTypes()
    {
        var commandType = $"CommandType_{nameof(CanPrefetchOldestCommandsOfSeveralTypes)}";
        var otherCommandType = $"OtherCommandType_{nameof(CanPrefetchOldestCommandsOfSeveralTypes)}";
        var commands = new[] { commandType, otherCommandType, commandType, otherCommandType }
            .Select((type, i) => new CommandRecord
            {
                Id = Guid.NewGuid(),
                CommandType = type,
                Data = "{\"test\": \"test\"}",
                CreatedAtUtc = new DateTime(2024, 03, 01, 10, i, 00, DateTimeKind.Utc),
            })
            .ToArray();
        var commandsStorage = CreateCommandsStorage();
        var workerStorage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, commands, CancellationToken.None));

        var fetchedCommands = await workerStorage.Prefetch(3, TimeSpan.FromMinutes(1),
            [commandType, otherCommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Select(x => x.Id), Is.EqualTo(commands.Take(3).Select(x => x.Id)));

        fetchedCommands = await workerStorage.Prefetch(3, TimeSpan.FromMinutes(1),
            [commandType, otherCommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Select(x => x.Id), Is.EqualTo(new[] { commands[3].Id }));
    }

    [Test]
    public void CannotPrefetchWithoutCommandTypes()
    {
        var workerStorage = CreateWorkerStorage();

        Assert.ThrowsAsync<ArgumentException>(() =>
            workerStorage.Prefetch(1, TimeSpan.FromMinutes(1), [], CancellationToken.None));
    }

    [Test]
    public async Task CanDeleteCommand()
    {
        var commandType = $"CommandType_{nameof(CanDeleteCommand)}";
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandStorage = CreateCommandsStorage();
        var workerStorage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        await workerStorage.Delete(command.Id, CancellationToken.None);

        var fetchedCommands =
            await workerStorage.Prefetch(1, TimeSpan.FromMilliseconds(500), [command.CommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task CannotPrefetchInvisibleCommands()
    {
        var commandType = $"CommandType_{nameof(CanDeleteCommand)}";
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandsStorage = CreateCommandsStorage();
        var workerStorage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        await workerStorage.MakeInvisibleFor(command.Id, TimeSpan.FromSeconds(30), CancellationToken.None);

        var fetchedCommands =
            await workerStorage.Prefetch(1, TimeSpan.FromMilliseconds(10), [command.CommandType], CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task CanGetSummary()
    {
        var commandType = $"CommandType_{nameof(CanGetSummary)}";
        var command = new CommandRecord
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            Data = "{\"test\": \"test\"}",
            CreatedAtUtc = new DateTime(2024, 03, 01, 10, 00, 00, DateTimeKind.Utc),
        };
        var commandsStorage = CreateCommandsStorage();
        var workerStorage = CreateWorkerStorage();

        await ExecuteInTransaction((connection, transaction) =>
            commandsStorage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var summary = await workerStorage.GetSummary(CancellationToken.None);

        Assert.That(summary.Count(), Is.EqualTo(1));
        var summaryLine = summary.Single();
        Assert.That(summaryLine.Count, Is.EqualTo(1));
        Assert.That(summaryLine.CommandType, Is.EqualTo(commandType));
    }
}