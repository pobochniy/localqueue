using System.Data;
using LocalQueue.Storage;
using MySqlConnector;

namespace LocalQueue.MySql;

internal class MySqlLocalCommandsQueueStorage : ILocalCommandsQueueStorage
{
    private readonly MySqlCommandQueueStorageOptions _options;

    public MySqlLocalCommandsQueueStorage(MySqlCommandQueueStorageOptions options)
    {
        _options = options;
    }

    public Task Create(IDbConnection connection, IDbTransaction transaction, IEnumerable<CommandRecord> commands,
        CancellationToken ct)
    {
        return SaveByChunks(connection, transaction, _options.TableName, commands.ToArray(), ct);
    }

    private async Task SaveByChunks(IDbConnection connection,
        IDbTransaction transaction,
        string table,
        CommandRecord[] commands,
        CancellationToken ct)
    {
        var chunks = commands.Chunk(_options.InsertBatchSize);
        foreach (var chunk in chunks)
        {
            var sql = @$"insert into {table} (Id, CommandType, Data, CreatedAtUtc, LockedTillUtc) 
                values {GetValues(chunk.Length)}";
            await ((MySqlConnection)connection).ExecuteNonQueryAsync(
                sql,
                GetParams(chunk),
                ct,
                (MySqlTransaction)transaction,
                _options.CommandTimeoutSeconds);
        }
    }

    private static string GetValues(int count)
    {
        return string.Join(",",
            Enumerable.Range(0, count).Select(i => $"(@Id{i}, @CommandType{i}, @Data{i}, @CreatedAtUtc{i}, @LockedTillUtc{i})"));
    }

    private static IEnumerable<MySqlParameter> GetParams(IReadOnlyList<CommandRecord> commands)
    {
        var res = new List<MySqlParameter>();
        for (var i = 0; i < commands.Count; i++)
        {
            res.Add(new MySqlParameter($"Id{i}", MySqlDbType.VarBinary) { Value = commands[i].Id.ToByteArray() });
            res.Add(new MySqlParameter($"CommandType{i}", MySqlDbType.VarBinary) { Value = commands[i].CommandType });
            res.Add(new MySqlParameter($"Data{i}", MySqlDbType.Int32) { Value = commands[i].Data });
            res.Add(new MySqlParameter($"CreatedAtUtc{i}", MySqlDbType.Int32) { Value = commands[i].CreatedAtUtc });
            res.Add(new MySqlParameter($"LockedTillUtc{i}", MySqlDbType.DateTime) { Value = commands[i].LockedTillUtc });
        }

        return res;
    }

    
}