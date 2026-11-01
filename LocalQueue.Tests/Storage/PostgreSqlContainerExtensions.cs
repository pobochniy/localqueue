using Npgsql;
using Testcontainers.PostgreSql;

namespace LocalQueue.Tests.Storage;

public static class PostgreSqlContainerExtensions
{
    public static PostgreSqlContainer BuildPostgreSqlContainer(string database)
    {
        return new PostgreSqlBuilder("postgres:17")
            .WithName($"local-command-queue-test-postgres-{Guid.NewGuid().ToString().Replace("-", "")[..8]}")
            .WithDatabase(database)
            .Build();
    }

    public static async Task CreateLocalQueueCommandsTable(this PostgreSqlContainer container, string tableName)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTable(tableName);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> CountRows(this PostgreSqlContainer container, string tableName)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(1) from {tableName}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    #region doc_create_table_script

    private static string CreateTable(string tableName) => $@"
                create table {tableName} (
                   id uuid not null primary key,
                   data jsonb not null,
                   command_type varchar(250) not null,
                   created_at_utc timestamptz not null,
                   locked_till_utc timestamptz,
                   try_count int not null default 0
                );
                create index on {tableName} (command_type, created_at_utc);";

    #endregion
}
