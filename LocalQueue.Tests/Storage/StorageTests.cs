using System.Data;
using LocalQueue.Storage;

namespace LocalQueue.Tests.Storage;

public abstract class StorageTests
{
    protected abstract ICommandsStorage CreateStorage();

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
        var storage = CreateStorage();

        await ExecuteInTransaction((connection, transaction) =>
            storage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), CancellationToken.None);

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
        var storage = CreateStorage();

        await ExecuteInTransaction((connection, transaction) =>
            storage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));

        fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), CancellationToken.None);
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
        var storage = CreateStorage();

        await ExecuteInTransaction((connection, transaction) =>
            storage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var fetchedCommands =
            await storage.Prefetch(1, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));
        await Task.Delay(TimeSpan.FromSeconds(2));

        fetchedCommands = await storage.Prefetch(1, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.That(fetchedCommands.Count(), Is.EqualTo(1));
        Assert.That(fetchedCommands.Single().TryCount, Is.EqualTo(2));
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
        var storage = CreateStorage();

        await ExecuteInTransaction((connection, transaction) =>
            storage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        await storage.Delete(command.Id, CancellationToken.None);

        var fetchedCommands =
            await storage.Prefetch(1, TimeSpan.FromMilliseconds(500), CancellationToken.None);
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
        var storage = CreateStorage();

        await ExecuteInTransaction((connection, transaction) =>
            storage.Create(connection, transaction, new[] { command }, CancellationToken.None));

        var summary = await storage.GetSummary(CancellationToken.None);

        Assert.That(summary.Count(), Is.EqualTo(1));
        var summaryLine = summary.Single();
        Assert.That(summaryLine.Count, Is.EqualTo(1));
        Assert.That(summaryLine.CommandType, Is.EqualTo(commandType));
    }
}