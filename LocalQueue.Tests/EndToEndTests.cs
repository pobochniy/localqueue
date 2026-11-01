using System.Collections.Concurrent;
using System.Data;
using LocalQueue.MySql;
using LocalQueue.PostgreSql;
using LocalQueue.Processing;
using LocalQueue.Queue;
using LocalQueue.Tests.Hosts;
using LocalQueue.Tests.Storage;
using MySqlConnector;
using Npgsql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace LocalQueue.Tests;

/// <summary>
/// The end to end scenarios run against two databases of the storage under test.
/// </summary>
public abstract class EndToEndTests
{
    protected const int Database = 0;
    protected const int OtherDatabase = 1;

    private static string TableName => $"localqueue_{TestContext.CurrentContext.Test.Name}";

    protected abstract Task StartDatabases();
    protected abstract Task StopDatabases();
    protected abstract string ConnectionString(int database);
    protected abstract Task CreateTable(int database, string tableName);
    protected abstract Task<long> CountRows(int database, string tableName);
    protected abstract Task ExecuteInTransaction(int database, Func<IDbConnection, IDbTransaction, Task> action);
    protected abstract void UseStorage(LocalCommandQueueConfigurator configurator, string tableName);
    protected abstract void UseWorkerStorage(LocalCommandQueueWorkerConfigurator configurator, string connectionString,
        string tableName);

    [OneTimeSetUp]
    public Task OneTimeSetup() => StartDatabases();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => StopDatabases();

    private StorageHost CreateStorageHost(string tableName) => new(c => UseStorage(c, tableName));

    [Test, CancelAfter(10_000)]
    public async Task CanProcessCommand()
    {
        await CreateTable(Database, TableName);

        using var storageHost = CreateStorageHost(TableName);
        await storageHost.StartAsync();

        using var workerHost = WorkerHost.Minimal(UseWorkerStorage, ConnectionString(Database), TableName);
        await workerHost.StartAsync();

        var command = await EnqueueCommand(storageHost, Database);

        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command);
        await AssertTableIsEmpty(TableName, Database);
        await storageHost.StopAsync();
        await workerHost.StopAsync();
    }

    [Test, CancelAfter(10_000)]
    public async Task CanProcessCommandsFromDifferentDatabases()
    {
        const string tableName = "twoconnections";
        await CreateTable(Database, tableName);
        await CreateTable(OtherDatabase, tableName);

        using var storageHost1 = CreateStorageHost(tableName);
        await storageHost1.StartAsync();

        using var storageHost2 = CreateStorageHost(tableName);
        await storageHost2.StartAsync();

        using var workerHost = WorkerHost.TwoDatabases(UseWorkerStorage,
            ConnectionString(Database), tableName,
            ConnectionString(OtherDatabase), tableName);
        await workerHost.StartAsync();

        var command1 = await EnqueueCommand(storageHost1, Database);
        var command2 = await EnqueueCommand(storageHost2, OtherDatabase);

        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command1);
        await handler.WaitCommandHandled(command2);
        await AssertTableIsEmpty(tableName, Database);
        await AssertTableIsEmpty(tableName, OtherDatabase);
        await storageHost1.StopAsync();
        await storageHost2.StopAsync();
        await workerHost.StopAsync();
    }

    [Test, CancelAfter(10_000)]
    public async Task CanProcessCommandTypesByOwnWorkers()
    {
        await CreateTable(Database, TableName);

        using var storageHost = CreateStorageHost(TableName);
        await storageHost.StartAsync();

        var lockLongerThanTest = TimeSpan.FromMinutes(1);
        var waitWithinLock = TimeSpan.FromSeconds(10);
        using var workerHost = WorkerHost.TwoWorkersOneTable(UseWorkerStorage, ConnectionString(Database), TableName,
            lockLongerThanTest);
        await workerHost.StartAsync();

        var otherCommand = new OtherLocalCommand();
        await EnqueueCommand(storageHost, Database, otherCommand);
        var command = await EnqueueCommand(storageHost, Database);

        await workerHost.GetRequiredService<LocalCommandHandler>().WaitCommandHandled(command).WaitAsync(waitWithinLock);
        await workerHost.GetRequiredService<OtherLocalCommandHandler>().WaitCommandHandled(otherCommand).WaitAsync(waitWithinLock);
        await AssertTableIsEmpty(TableName, Database);
        await storageHost.StopAsync();
        await workerHost.StopAsync();
    }

    [Test, CancelAfter(10_000)]
    public async Task CanGetWorkerMetrics()
    {
        await CreateTable(Database, TableName);

        using var storageHost = CreateStorageHost(TableName);
        await storageHost.StartAsync();

        using var workerHost = WorkerHost.Minimal(UseWorkerStorage, ConnectionString(Database), TableName);
        await workerHost.StartAsync();

        var command = await EnqueueCommand(storageHost, Database);

        var handler = workerHost.GetRequiredService<LocalCommandHandler>();
        await handler.WaitCommandHandled(command);

        // The processing metrics are recorded after the handler returns and the command is deleted
        string[] metrics =
        [
            "local_command_queue_max_try_count",
            "local_command_queue_length",
            "local_command_queue_processing_time",
            "local_command_queue_processing_lag",
            "local_command_queue_completed"
        ];
        var metricsText = string.Empty;
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (!timeout.IsCompleted)
        {
            metricsText = await workerHost.GetTestClient().GetStringAsync("/metrics");
            if (metrics.All(metricsText.Contains))
            {
                break;
            }

            await Task.Delay(200);
        }

        Assert.Multiple(() =>
        {
            foreach (var metric in metrics)
            {
                Assert.That(metricsText, Contains.Substring(metric));
            }
        });
    }

    private async Task<LocalCommand> EnqueueCommand(StorageHost commandQueueHost, int database)
    {
        var command = new LocalCommand();
        await EnqueueCommand(commandQueueHost, database, command);
        return command;
    }

    private Task EnqueueCommand<TCommand>(StorageHost commandQueueHost, int database, TCommand command)
        where TCommand : notnull
    {
        var commandsQueue = commandQueueHost.GetRequiredService<ICommandsQueue>();
        return ExecuteInTransaction(database, (connection, transaction) =>
            commandsQueue.Enqueue(connection, transaction, new[] { command }, CancellationToken.None));
    }

    private async Task AssertTableIsEmpty(string tableName, int database)
    {
        long? tableLength = default;
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (!timeout.IsCompleted)
        {
            tableLength = await CountRows(database, tableName);
            if (tableLength == 0)
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.That(tableLength, Is.EqualTo(0));
    }
}

public class MySqlEndToEndTests : EndToEndTests
{
    private readonly MySqlContainer[] _databases = [BuildContainer(), BuildContainer()];

    private static MySqlContainer BuildContainer() => new MySqlBuilder("mysql:9.7")
        .WithName($"local-command-queue-test-mysql-{Guid.NewGuid().ToString().Replace("-", "")[..8]}")
        .WithDatabase("endtoend")
        .WithCommand("--sql_require_primary_key=ON")
        .Build();

    protected override Task StartDatabases() => Task.WhenAll(_databases.Select(d => d.StartAsync()));

    protected override async Task StopDatabases()
    {
        foreach (var database in _databases)
        {
            await database.StopAsync();
            await database.DisposeAsync();
        }
    }

    protected override string ConnectionString(int database) => _databases[database].GetConnectionString();

    protected override Task CreateTable(int database, string tableName) =>
        _databases[database].CreateLocalQueueCommandsTable(tableName);

    protected override async Task<long> CountRows(int database, string tableName)
    {
        await using var connection = new MySqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>($"select count(1) from {tableName}");
    }

    protected override async Task ExecuteInTransaction(int database, Func<IDbConnection, IDbTransaction, Task> action)
    {
        await using var connection = new MySqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await action(connection, transaction);
        await transaction.CommitAsync();
    }

    protected override void UseStorage(LocalCommandQueueConfigurator configurator, string tableName) =>
        configurator.UseMySqlStorage(new MySqlCommandQueueStorageOptions { TableName = tableName });

    protected override void UseWorkerStorage(LocalCommandQueueWorkerConfigurator configurator, string connectionString,
        string tableName) =>
        configurator.UseMySqlStorage(options =>
        {
            options.ConnectionString = connectionString;
            options.TableName = tableName;
        });
}

public class PostgreSqlEndToEndTests : EndToEndTests
{
    private readonly PostgreSqlContainer[] _databases =
    [
        PostgreSqlContainerExtensions.BuildPostgreSqlContainer("endtoend"),
        PostgreSqlContainerExtensions.BuildPostgreSqlContainer("endtoend")
    ];

    protected override Task StartDatabases() => Task.WhenAll(_databases.Select(d => d.StartAsync()));

    protected override async Task StopDatabases()
    {
        foreach (var database in _databases)
        {
            await database.StopAsync();
            await database.DisposeAsync();
        }
    }

    protected override string ConnectionString(int database) => _databases[database].GetConnectionString();

    protected override Task CreateTable(int database, string tableName) =>
        _databases[database].CreateLocalQueueCommandsTable(tableName);

    protected override Task<long> CountRows(int database, string tableName) =>
        _databases[database].CountRows(tableName);

    protected override async Task ExecuteInTransaction(int database, Func<IDbConnection, IDbTransaction, Task> action)
    {
        await using var connection = new NpgsqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await action(connection, transaction);
        await transaction.CommitAsync();
    }

    protected override void UseStorage(LocalCommandQueueConfigurator configurator, string tableName) =>
        configurator.UsePostgreSqlStorage(new PostgreSqlCommandQueueStorageOptions { TableName = tableName });

    protected override void UseWorkerStorage(LocalCommandQueueWorkerConfigurator configurator, string connectionString,
        string tableName) =>
        configurator.UsePostgreSqlStorage(options =>
        {
            options.ConnectionString = connectionString;
            options.TableName = tableName;
        });
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
