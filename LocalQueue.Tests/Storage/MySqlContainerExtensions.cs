using MySqlConnector;
using Testcontainers.MySql;

namespace LocalQueue.Tests.Storage;

public static class MySqlContainerExtensions
{
    public static async Task CreateLocalQueueCommandsTable(this MySqlContainer container, string tableName)
    {
        await using var connection = new MySqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTable(tableName);
        await command.ExecuteNonQueryAsync();
    }

    #region doc_create_table_script

    private static string CreateTable(string tableName) => @$"
                create table {tableName} (
                   Id binary(16) not null primary key ,
                   `Data` json NOT NULL,
                   CommandType varchar(250) not null,
                   CreatedAtUtc datetime not null,
                   LockedTillUtc datetime,
                   TryCount int not null default(0),
                   index `CommandType_CreatedAtUtc_IDX` (`CommandType`, `CreatedAtUtc`)
                );";

    #endregion
}