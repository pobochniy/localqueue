using MySqlConnector;

namespace LocalQueue.Tests.Storage;

internal static class MySqlConnectionExtensions
{
    public static async Task<T?> ExecuteScalarAsync<T>(this MySqlConnection connection,
        string sql, IEnumerable<MySqlParameter>? parameters = null,
        MySqlTransaction? transaction = null,
        int commandTimeout = 2,
        CancellationToken ct = default)
    {
        return (T?) await BuildCommand(connection, sql, parameters ?? Array.Empty<MySqlParameter>(), transaction, commandTimeout).ExecuteScalarAsync(ct);
    }
    
    private static MySqlCommand BuildCommand(MySqlConnection connection,
        string sql,
        IEnumerable<MySqlParameter> parameters,
        MySqlTransaction? transaction,
        int commandTimeout)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeout;
        command.Transaction = transaction;

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }
}