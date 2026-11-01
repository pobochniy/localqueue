using System.Data;
using LocalQueue.PostgreSql;
using LocalQueue.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LocalQueue.Tests.Storage;

[TestFixture]
public class PostgreSqlLocalCommandsQueueStorageTests : StorageTests
{
    private readonly PostgreSqlContainer _database =
        PostgreSqlContainerExtensions.BuildPostgreSqlContainer("postgresqlstoragetest");

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
            var samePooledTableStorage = CreateWorkerStorage(TableName, _database.GetConnectionString() + ";Pooling=false");
            Assert.That(storage, Is.EqualTo(samePooledTableStorage));
            Assert.That(storage.GetHashCode(), Is.EqualTo(samePooledTableStorage.GetHashCode()));
            // unquoted names are folded to lower case
            var table = TableName.ToLowerInvariant();
            foreach (var sameTableName in new[]
                     {
                         TableName.ToUpperInvariant(), $"\"{table}\"", $"public.{TableName}", $"\"public\".\"{table}\""
                     })
            {
                var sameTableNameStorage = CreateWorkerStorage(sameTableName, _database.GetConnectionString());
                Assert.That(storage, Is.EqualTo(sameTableNameStorage), sameTableName);
                Assert.That(storage.GetHashCode(), Is.EqualTo(sameTableNameStorage.GetHashCode()), sameTableName);
            }

            Assert.That(storage, Is.Not.EqualTo(CreateWorkerStorage($"\"{TableName}\"", _database.GetConnectionString())));
            Assert.That(storage, Is.Not.EqualTo(CreateWorkerStorage($"other_schema.{TableName}", _database.GetConnectionString())));
            Assert.That(storage, Is.Not.EqualTo(CreateWorkerStorage(TableName, _database.GetConnectionString() + ";Database=other_database")));
            Assert.That(storage, Is.Not.EqualTo(CreateWorkerStorage(TableName, _database.GetConnectionString() + ";Host=other-host")));
        });
    }

    protected override ILocalCommandsQueueWorkerStorage CreateWorkerStorage()
    {
        return CreateWorkerStorage(TableName, _database.GetConnectionString());
    }

    private static PostgreSqlLocalCommandsQueueWorkerStorage CreateWorkerStorage(string tableName, string connectionString)
    {
        return new PostgreSqlLocalCommandsQueueWorkerStorage(new PostgreSqlLocalCommandsQueueWorkerStorageOptions
        {
            TableName = tableName,
            ConnectionString = connectionString
        });
    }

    protected override ILocalCommandsQueueStorage CreateCommandsStorage()
    {
        return new PostgreSqlLocalCommandsQueueStorage(new PostgreSqlCommandQueueStorageOptions
        {
            TableName = TableName,
        });
    }

    protected override async Task ExecuteInTransaction(Func<IDbConnection, IDbTransaction, Task> action,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await action(connection, transaction);
        await transaction.CommitAsync(ct);
    }
}
