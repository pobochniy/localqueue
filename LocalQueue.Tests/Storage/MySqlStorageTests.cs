using System.Data;
using LocalQueue.Storage;
using LocalQueue.MySql;
using MySqlConnector;
using Testcontainers.MySql;

namespace LocalQueue.Tests.Storage;

[TestFixture]
public class MySqlStorageTests : StorageTests
{
    private readonly MySqlContainer _database = new MySqlBuilder()
        .WithImage("mysql:8.0")
        .WithName($"local-command-queue-test-mysql-{Guid.NewGuid().ToString().Replace("-", "")[..8]}")
        .WithDatabase($"mysqlstoragetest")
        .WithCommand("--sql_require_primary_key=ON")
        .Build();

    private static string TableName => TestContext.CurrentContext.Test.Name;

    [OneTimeSetUp]
    public Task OneTimeSetUp()
    {
        return _database.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _database.StopAsync();
        await _database.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await _database.CreateLocalQueueCommandsTable(TableName);
    }

    protected override ICommandsStorage CreateStorage()
    {
        return new MySqlCommandStorage(new CommandStorageOptions
        {
            TableName = TableName,
            ConnectionString = _database.GetConnectionString()
        });
    }

    protected override async Task ExecuteInTransaction(Func<IDbConnection, IDbTransaction, Task> action,
        CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(_database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken: ct);
        await action(connection, transaction);
        await transaction.CommitAsync(ct);
    }
}