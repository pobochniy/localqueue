using System.Collections.Concurrent;
using LocalQueue.Processing;
using LocalQueue.Queue;
using LocalQueue.Tests.Hosts;
using LocalQueue.Tests.Storage;
using MySqlConnector;
using Testcontainers.MySql;

namespace LocalQueue.Tests;

public class EndToEndTest
{
    private readonly MySqlContainer _database = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithName($"local-command-queue-test-mysql-{Guid.NewGuid().ToString().Replace("-", "")[..8]}")
        .WithDatabase($"endtoend")
        .WithCommand("--sql_require_primary_key=ON")
        .Build();

    private static string TableName => $"localqueue_{TestContext.CurrentContext.Test.Name}";

    [OneTimeSetUp]
    public Task OneTimeSetup()
    {
        return _database.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _database.StopAsync();
        await _database.DisposeAsync();
    }

    [Test, Timeout(10_000)]
    public async Task CanProcessCommand()
    {
        await _database.CreateLocalQueueCommandsTable(TableName);
        using var storageHost = new MinimalMySqlStorageHost(_database.GetConnectionString(), TableName);
        await storageHost.StartAsync();
        using var workerHost = new MinimalMySqlWorkerHost(_database.GetConnectionString(), TableName);
        await workerHost.StartAsync();

        var commandsQueue = storageHost.GetRequiresService<ICommandsQueue>();

        await using var connection = new MySqlConnection(_database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync();
        var command = new LocalCommand();
        await commandsQueue.Enqueue(connection, transaction, new[] { command }, CancellationToken.None);
        await transaction.CommitAsync();

        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command);
        await AssertTableIsEmpty(TableName);
        await storageHost.StopAsync();
        await workerHost.StopAsync();
    }
    
    private async Task AssertTableIsEmpty(string tableName)
    {
        await using var connection = new MySqlConnection(_database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        
        long? tableLength = default;
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (!timeout.IsCompleted)
        {
            tableLength = await connection.ExecuteScalarAsync<long>($"select count(1) from {tableName}");
            if (tableLength == 0)
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.That(tableLength, Is.EqualTo(0));
    }
}

public class LocalCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public class LocalCommandHandler : ICommandHandler<LocalCommand>
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> handledEvents = new();

    public Task Handle(LocalCommand command, CancellationToken ct)
    {
        var taskCompletionSource = handledEvents.GetOrAdd(command.Id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        taskCompletionSource.SetResult();
        return Task.CompletedTask;
    }

    public Task WaitCommandHandled(LocalCommand command)
    {
        var taskCompletionSource = handledEvents.GetOrAdd(command.Id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        return taskCompletionSource.Task;
    }
}