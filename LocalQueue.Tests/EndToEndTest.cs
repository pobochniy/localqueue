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
    private readonly MySqlContainer _database = new MySqlBuilder("mysql:8.0")
        .WithName($"local-command-queue-test-mysql-{Guid.NewGuid().ToString().Replace("-", "")[..8]}")
        .WithDatabase($"endtoend")
        .WithCommand("--sql_require_primary_key=ON")
        .Build();
    
    private readonly MySqlContainer _otherDatabase = new MySqlBuilder("mysql:8.0")
        .WithName($"local-command-queue-test-mysql-{Guid.NewGuid().ToString().Replace("-", "")[..8]}")
        .WithDatabase($"endtoend")
        .WithCommand("--sql_require_primary_key=ON")
        .Build();

    private static string TableName => $"localqueue_{TestContext.CurrentContext.Test.Name}";

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        await _database.StartAsync();
        await  _otherDatabase.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _database.StopAsync();
        await _database.DisposeAsync();
        await _otherDatabase.StopAsync();
        await _otherDatabase.DisposeAsync();
    }

    [Test, CancelAfter(10_000)]
    public async Task CanProcessCommand()
    {
        await _database.CreateLocalQueueCommandsTable(TableName);
        
        using var storageHost = new MinimalMySqlStorageHost(TableName);
        await storageHost.StartAsync();
        
        using var workerHost = MySqlWorkerHost.Minimal(_database.GetConnectionString(), TableName);
        await workerHost.StartAsync();

        var command = await EnqueueCommand(storageHost, _database);
        
        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command);
        await AssertTableIsEmpty(TableName, _database);
        await storageHost.StopAsync();
        await workerHost.StopAsync();
    }
    
    [Test, CancelAfter(10_000)]
    public async Task CanProcessCommandsFromDifferentDatabases()
    {
        string tableName = "twoconnections";
        await _database.CreateLocalQueueCommandsTable(tableName);
        await _otherDatabase.CreateLocalQueueCommandsTable(tableName);
        
        using var storageHost1 = new MinimalMySqlStorageHost(tableName);
        await storageHost1.StartAsync();
        
        using var storageHost2 = new MinimalMySqlStorageHost(tableName);
        await storageHost2.StartAsync();
        
        using var workerHost = MySqlWorkerHost.TwoDatabases(
            _database.GetConnectionString(), tableName ,
            _otherDatabase.GetConnectionString(), tableName);
        await workerHost.StartAsync();

        var command1 = await EnqueueCommand(storageHost1, _database);
        var command2 = await EnqueueCommand(storageHost2, _otherDatabase);

        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command1);
        await handler.WaitCommandHandled(command2);
        await AssertTableIsEmpty(tableName, _database);
        await AssertTableIsEmpty(tableName, _otherDatabase);
        await storageHost1.StopAsync();
        await storageHost2.StopAsync();
        await workerHost.StopAsync();
    }
    
    [Test, CancelAfter(10_000)]
    public async Task CanProcessCommandTypesByOwnWorkers()
    {
        await _database.CreateLocalQueueCommandsTable(TableName);

        using var storageHost = new MinimalMySqlStorageHost(TableName);
        await storageHost.StartAsync();

        var lockLongerThanTest = TimeSpan.FromMinutes(1);
        var waitWithinLock = TimeSpan.FromSeconds(10);
        using var workerHost = MySqlWorkerHost.TwoWorkersOneTable(_database.GetConnectionString(), TableName, lockLongerThanTest);
        await workerHost.StartAsync();

        var otherCommand = new OtherLocalCommand();
        await EnqueueCommand(storageHost, _database, otherCommand);
        var command = await EnqueueCommand(storageHost, _database);

        await workerHost.GetRequiredService<LocalCommandHandler>().WaitCommandHandled(command).WaitAsync(waitWithinLock);
        await workerHost.GetRequiredService<OtherLocalCommandHandler>().WaitCommandHandled(otherCommand).WaitAsync(waitWithinLock);
        await AssertTableIsEmpty(TableName, _database);
        await storageHost.StopAsync();
        await workerHost.StopAsync();
    }

    [Test, CancelAfter(10_000)]
    public async Task CanGetWorkerMetrics()
    {
        await _database.CreateLocalQueueCommandsTable(TableName);
        
        using var storageHost = new MinimalMySqlStorageHost(TableName);
        await storageHost.StartAsync();
        
        using var workerHost = MySqlWorkerHost.Minimal(_database.GetConnectionString(), TableName);
        await workerHost.StartAsync();

        var command = await EnqueueCommand(storageHost, _database);
        
        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command);

        var metricsJson = await workerHost.GetTestClient().GetStringAsync("/metrics");
        Assert.That(metricsJson, Contains.Substring("local_command_queue_max_try_count"));
        Assert.That(metricsJson, Contains.Substring("local_command_queue_length"));
        Assert.That(metricsJson, Contains.Substring("local_command_queue_processing_time"));
        Assert.That(metricsJson, Contains.Substring("local_command_queue_processing_lag"));
        Assert.That(metricsJson, Contains.Substring("local_command_queue_completed"));
    }

    private static async Task<LocalCommand> EnqueueCommand(MinimalMySqlStorageHost commandQueueHost, MySqlContainer mySqlContainer)
    {
        var command = new LocalCommand();
        await EnqueueCommand(commandQueueHost, mySqlContainer, command);
        return command;
    }

    private static async Task EnqueueCommand<TCommand>(MinimalMySqlStorageHost commandQueueHost, MySqlContainer mySqlContainer,
        TCommand command) where TCommand : notnull
    {
        var commandsQueue = commandQueueHost.GetRequiresService<ICommandsQueue>();
        await using var connection = new MySqlConnection(mySqlContainer.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync();
        await commandsQueue.Enqueue(connection, transaction, new[] { command }, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static async Task AssertTableIsEmpty(string tableName, MySqlContainer mySqlContainer)
    {
        await using var connection = new MySqlConnection(mySqlContainer.GetConnectionString());
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

public abstract class TestCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public class LocalCommand : TestCommand;

public class OtherLocalCommand : TestCommand;

public class LocalCommandHandler : TestCommandHandler<LocalCommand>;

public class OtherLocalCommandHandler : TestCommandHandler<OtherLocalCommand>;

public abstract class TestCommandHandler<TCommand> : ICommandHandler<TCommand> where TCommand : TestCommand
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> handledEvents = new();

    public Task Handle(TCommand command, CancellationToken ct)
    {
        var taskCompletionSource = handledEvents.GetOrAdd(command.Id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        taskCompletionSource.SetResult();
        return Task.CompletedTask;
    }

    public Task WaitCommandHandled(TCommand command)
    {
        var taskCompletionSource = handledEvents.GetOrAdd(command.Id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        return taskCompletionSource.Task;
    }
}
