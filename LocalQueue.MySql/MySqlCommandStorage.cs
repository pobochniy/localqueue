using System.Data;
using LocalQueue.Diagnostics;
using LocalQueue.Storage;
using MySqlConnector;

namespace LocalQueue.MySql;

internal class MySqlCommandStorage : ICommandsStorage
{
    private readonly CommandStorageOptions _options;

    public MySqlCommandStorage(CommandStorageOptions options)
    {
        _options = options;
    }

    public Task Create(IDbConnection connection, IDbTransaction transaction, IEnumerable<CommandRecord> commands,
        CancellationToken ct)
    {
        return SaveByChunks(connection, transaction, _options.TableName, commands.ToArray(), ct);
    }

    private async Task SaveByChunks(System.Data.IDbConnection connection,
        IDbTransaction transaction,
        string table,
        CommandRecord[] commands,
        CancellationToken ct)
    {
        var chunks = commands.Chunk(_options.InsertBatchSize);
        foreach (var chunk in chunks)
        {
            var sql = @$"insert into {table} (Id, CommandType, Data, CreatedAtUtc) 
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
            Enumerable.Range(0, count).Select(i => $"(@Id{i}, @CommandType{i}, @Data{i}, @CreatedAtUtc{i})"));
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
        }

        return res;
    }

    public async Task<IEnumerable<CommandRecord>> Prefetch(int count, TimeSpan timeout,
        CancellationToken ct)
    {
        var utcNow = DateTime.UtcNow;
        var lockTillUtc = utcNow + timeout;

        await using var connection = new MySqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var list = await PrefetchCommands(command, count, lockTillUtc, ct);

        if (list.Any())
        {
            var commandIdsParameters = list.Select((command, i) =>
                new MySqlParameter($"@id{i}", MySqlDbType.Binary) { Value = command.Id.ToByteArray() });
            var commandIdsParametersNames = string.Join(", ", commandIdsParameters.Select(x => x.ParameterName));

            command.CommandType = CommandType.Text;
            command.Parameters.Clear();
            command.Transaction = transaction;
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            command.CommandText =
                $@"update {_options.TableName}
			set LockedTillUtc = @lockedTill, TryCount = TryCount + 1
            where Id in ({commandIdsParametersNames})";
            command.Parameters.AddRange(commandIdsParameters.ToArray());
            command.Parameters.Add("@lockedTill", DbType.DateTime).Value = lockTillUtc;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return list;
    }

    private async Task<List<CommandRecord>> PrefetchCommands(MySqlCommand command,
        int count,
        DateTime lockTillUtc,
        CancellationToken ct)
    {
        command.Parameters.Clear();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.CommandText =
            $@"select Id, CommandType, Data, CreatedAtUtc, TryCount + 1 TryCount
            from {_options.TableName}
            where LockedTillUtc is null or LockedTillUtc < utc_timestamp()
            order by CreatedAtUtc
            limit {count}
            FOR UPDATE SKIP LOCKED";

        var list = new List<CommandRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = new Guid(reader.GetBytes("Id"));
            var commandType = reader.GetString("CommandType");
            var data = reader.GetString("Data");
            var createdAt = reader.GetDateTime("CreatedAtUtc");
            var tryCount = reader.GetInt32("TryCount");

            list.Add(new CommandRecord
            {
                Id = id,
                CommandType = commandType,
                CreatedAtUtc = createdAt,
                Data = data,
                LockedTillUtc = lockTillUtc,
                TryCount = tryCount
            });
        }

        return list;
    }

    public async Task Delete(Guid id, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var parameters = new[]
        {
            new MySqlParameter("@id", MySqlDbType.Binary) { Value = id.ToByteArray() }
        };

        await connection.ExecuteNonQueryAsync(
            @$"delete from {_options.TableName} where Id = @id",
            parameters,
            ct,
            commandTimeout: _options.CommandTimeoutSeconds);
    }

    public async Task<IEnumerable<LocalQueueStorageSummary>> GetSummary(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandTimeout = _options.CommandTimeoutSeconds;
        selectCommand.CommandText =
            $@"select CommandType, count(*) as Count, max(TryCount) as MaxTryCount
 			from {_options.TableName} group by CommandType";

        var result = new List<LocalQueueStorageSummary>();
        await using var reader = await selectCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var commandType = reader.GetString("CommandType");
            var count = reader.GetInt32("Count");
            var maxTryCount = reader.GetInt32("MaxTryCount");

            result.Add(new LocalQueueStorageSummary
            {
                CommandType = commandType,
                Count = count,
                MaxTryCount = maxTryCount
            });
        }

        return result;
    }
}