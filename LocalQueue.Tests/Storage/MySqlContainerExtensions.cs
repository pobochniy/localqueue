using Testcontainers.MySql;

namespace LocalQueue.Tests.Storage;

public static class MySqlContainerExtensions
{
    public static Task CreateLocalQueueCommandsTable(this MySqlContainer container, string tableName)
    {
        return container.ExecScriptAsync(createTable(tableName));
    }

    #region doc_create_table_script

    private static string createTable(string tableName) => @$"
                create table {tableName} (
                   Id binary(16) not null primary key ,
                   `Data` json NOT NULL,
                   CommandType varchar(250) not null,
                   CreatedAtUtc datetime not null,
                   LockedTillUtc datetime,
                   TryCount int not null default(0)
                );";

    #endregion
}