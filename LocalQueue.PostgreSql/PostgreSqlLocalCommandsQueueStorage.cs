using System.Data;
using LocalQueue.Storage;
using Npgsql;
using NpgsqlTypes;

namespace LocalQueue.PostgreSql;

internal class PostgreSqlLocalCommandsQueueStorage(PostgreSqlCommandQueueStorageOptions options)
    : ILocalCommandsQueueStorage
{
    public async Task Create(IDbConnection connection, IDbTransaction transaction, IEnumerable<CommandRecord> commands,
        CancellationToken ct)
    {
        foreach (var chunk in commands.Chunk(options.InsertBatchSize))
        {
            var sql = $@"insert into {options.TableName} (id, command_type, data, created_at_utc, locked_till_utc)
                values {GetValues(chunk.Length)}";
            await ((NpgsqlConnection)connection).ExecuteNonQueryAsync(
                sql,
                GetParams(chunk),
                ct,
                (NpgsqlTransaction)transaction,
                options.CommandTimeoutSeconds);
        }
    }

    private static string GetValues(int count)
    {
        return string.Join(",",
            Enumerable.Range(0, count).Select(i =>
                $"(@Id{i}, @CommandType{i}, @Data{i}, @CreatedAtUtc{i}, @LockedTillUtc{i})"));
    }

    private static IEnumerable<NpgsqlParameter> GetParams(IReadOnlyList<CommandRecord> commands)
    {
        var res = new List<NpgsqlParameter>(commands.Count * 5);
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            res.Add(new NpgsqlParameter($"Id{i}", NpgsqlDbType.Uuid) { Value = command.Id });
            res.Add(new NpgsqlParameter($"CommandType{i}", NpgsqlDbType.Varchar) { Value = command.CommandType });
            res.Add(new NpgsqlParameter($"Data{i}", NpgsqlDbType.Jsonb) { Value = command.Data });
            res.Add(new NpgsqlParameter($"CreatedAtUtc{i}", NpgsqlDbType.TimestampTz)
                { Value = command.CreatedAtUtc.AsUtc() });
            res.Add(new NpgsqlParameter($"LockedTillUtc{i}", NpgsqlDbType.TimestampTz)
                { Value = command.LockedTillUtc is { } lockedTill ? lockedTill.AsUtc() : DBNull.Value });
        }

        return res;
    }
}
