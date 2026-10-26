using System.Data;
using LocalQueue.MySql;
using LocalQueue.Storage;
using MySqlConnector;
using Testcontainers.MySql;

namespace LocalQueue.Tests.Storage;

[TestFixture]
public class MySqlLocalCommandsQueueStorageTests : StorageTests
{
    private readonly MySqlContainer _database = new MySqlBuilder("mysql:8.0")
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

    [Test]
    public void StoragesOfTheSameTableAreEqual()
    {
        var storage = CreateWorkerStorage();
        var sameTableStorage = CreateWorkerStorage();

        Assert.Multiple(() =>
        {
            Assert.That(storage, Is.EqualTo(sameTableStorage));
            Assert.That(storage.GetHashCode(), Is.EqualTo(sameTableStorage.GetHashCode()));
            var samePooledTableStorage = new MySqlLocalCommandsQueueWorkerStorage(
                new MySqlLocalCommandsQueueWorkerStorageOptions
                {
                    TableName = TableName,
                    ConnectionString = _database.GetConnectionString() + ";Pooling=false"
                });
            Assert.That(storage, Is.EqualTo(samePooledTableStorage));
            Assert.That(storage.GetHashCode(), Is.EqualTo(samePooledTableStorage.GetHashCode()));
            var database = new MySqlConnectionStringBuilder(_database.GetConnectionString()).Database;
            foreach (var sameTableName in new[] { $"`{TableName}`", $"{database}.{TableName}", $"`{database}`.`{TableName}`" })
            {
                var sameTableNameStorage = new MySqlLocalCommandsQueueWorkerStorage(
                    new MySqlLocalCommandsQueueWorkerStorageOptions
                    {
                        TableName = sameTableName,
                        ConnectionString = _database.GetConnectionString()
                    });
                Assert.That(storage, Is.EqualTo(sameTableNameStorage), sameTableName);
                Assert.That(storage.GetHashCode(), Is.EqualTo(sameTableNameStorage.GetHashCode()), sameTableName);
            }

            Assert.That(storage, Is.Not.EqualTo(new MySqlLocalCommandsQueueWorkerStorage(
                new MySqlLocalCommandsQueueWorkerStorageOptions
                {
                    TableName = $"other_database.{TableName}",
                    ConnectionString = _database.GetConnectionString()
                })));
            Assert.That(storage, Is.Not.EqualTo(new MySqlLocalCommandsQueueWorkerStorage(
                new MySqlLocalCommandsQueueWorkerStorageOptions
                {
                    TableName = TableName,
                    ConnectionString = _database.GetConnectionString() + ";Server=other-host"
                })));
        });
    }

    protected override ILocalCommandsQueueWorkerStorage CreateWorkerStorage()
    {
        return new MySqlLocalCommandsQueueWorkerStorage(new MySqlLocalCommandsQueueWorkerStorageOptions
        {
            TableName = TableName,
            ConnectionString = _database.GetConnectionString()
        });
    }

    protected override ILocalCommandsQueueStorage CreateCommandsStorage()
    {
        return new MySqlLocalCommandsQueueStorage(new MySqlCommandQueueStorageOptions
        {
            TableName = TableName,
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
