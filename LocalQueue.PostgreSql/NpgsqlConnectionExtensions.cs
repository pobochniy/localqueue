using Npgsql;

namespace LocalQueue.PostgreSql;

internal static class NpgsqlConnectionExtensions
{
    // ReSharper disable once UnusedMethodReturnValue.Global
    public static async Task<int> ExecuteNonQueryAsync(this NpgsqlConnection connection,
        string sql,
        IEnumerable<NpgsqlParameter> parameters,
        CancellationToken ct,
        NpgsqlTransaction? transaction = null,
        int commandTimeout = 5)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeout;
        command.Transaction = transaction;
        command.Parameters.AddRange(parameters.ToArray());

        return await command.ExecuteNonQueryAsync(ct);
    }

    public static DateTime AsUtc(this DateTime dateTime) => dateTime.Kind switch
    {
        DateTimeKind.Utc => dateTime,
        DateTimeKind.Local => dateTime.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
    };
}
