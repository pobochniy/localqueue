using System.Data;
using LocalQueue.Diagnostics;
using LocalQueue.Storage;
using MySqlConnector;

namespace LocalQueue.MySql;

internal class MySqlLocalCommandsQueueWorkerStorage(MySqlLocalCommandsQueueWorkerStorageOptions options)
    : ILocalCommandsQueueWorkerStorage
{
    // The table identity: connection strings that differ in key order or pool settings lead to the same table
    private readonly (string Server, uint Port, string Database, string Table) _table = TableIdentity(options);

    public async Task<IEnumerable<CommandRecord>> Prefetch(int count, TimeSpan timeout,
        IReadOnlyCollection<string> commandTypes, CancellationToken ct)
    {
        if (commandTypes.Count == 0)
        {
            throw new ArgumentException("At least one command type must be specified.", nameof(commandTypes));
        }

        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var (lockTillUtc, list) = await PrefetchCommands(command, count, timeout, commandTypes, ct);

        if (list.Any())
        {
            var commandIdsParameters = list.Select((command, i) =>
                new MySqlParameter($"@id{i}", MySqlDbType.Binary) { Value = command.Id.ToByteArray() });
            var commandIdsParametersNames = string.Join(", ", commandIdsParameters.Select(x => x.ParameterName));

            command.CommandType = CommandType.Text;
            command.Parameters.Clear();
            command.Transaction = transaction;
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.CommandText =
                $@"update {options.TableName}
			set LockedTillUtc = @lockedTill, TryCount = TryCount + 1
            where Id in ({commandIdsParametersNames})";
            command.Parameters.AddRange(commandIdsParameters.ToArray());
            command.Parameters.Add("@lockedTill", DbType.DateTime).Value = lockTillUtc;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return list;
    }

    private async Task<(DateTime, List<CommandRecord>)> PrefetchCommands(MySqlCommand command,
        int count,
        TimeSpan timeout,
        IReadOnlyCollection<string> commandTypes,
        CancellationToken ct)
    {
        var commandTypesParameters = commandTypes.Select((commandType, i) =>
            new MySqlParameter($"@commandType{i}", MySqlDbType.VarChar) { Value = commandType }).ToArray();
        // A query per type: with `CommandType in (...)` the index can't give the order, all matching rows get locked
        var commandTypesQueries = commandTypesParameters.Select(parameter => $@"
            (select Id, CommandType, Data, CreatedAtUtc, TryCount + 1 TryCount
            from {options.TableName}
            where CommandType = {parameter.ParameterName}
                and (LockedTillUtc is null or LockedTillUtc < utc_timestamp())
            order by CreatedAtUtc
            limit {count}
            FOR UPDATE SKIP LOCKED)");

        command.Parameters.Clear();
        command.Parameters.Add("@timeout", DbType.DateTime).Value = (int)timeout.TotalMilliseconds;
        command.Parameters.AddRange(commandTypesParameters);
        command.CommandType = CommandType.Text;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $@"
            select utc_timestamp() + interval (@timeout * 1000) microsecond as LockedTillUtc;
            {string.Join(" union all ", commandTypesQueries)}
            order by CreatedAtUtc
            limit {count}";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        
        await reader.ReadAsync(ct).ConfigureAwait(false);
        var lockTillUtc = reader.GetDateTime("LockedTillUtc");
        await reader.NextResultAsync(ct);
        
        var list = new List<CommandRecord>(count);
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

        return (lockTillUtc, list);
    }

    public async Task Delete(Guid id, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var parameters = new[]
        {
            new MySqlParameter("@id", MySqlDbType.Binary) { Value = id.ToByteArray() }
        };

        await connection.ExecuteNonQueryAsync(
            @$"delete from {options.TableName} where Id = @id",
            parameters,
            ct,
            commandTimeout: options.CommandTimeoutSeconds);
    }

    public async Task<IEnumerable<LocalQueueStorageSummary>> GetSummary(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandTimeout = options.CommandTimeoutSeconds;
        selectCommand.CommandText =
            $@"select CommandType, count(*) as Count, max(TryCount) as MaxTryCount
 			from {options.TableName} group by CommandType";

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

    public async Task MakeInvisibleFor(Guid id, TimeSpan timeout, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.Parameters.Clear();
        command.Parameters.Add("@id", MySqlDbType.Binary).Value = id.ToByteArray();
        command.Parameters.Add("@timeout", DbType.Int32).Value = (int)timeout.TotalMilliseconds;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText =
            $@"update {options.TableName}
			set LockedTillUtc = utc_timestamp() + interval (@timeout * 1000) microsecond
            where Id = @id";
        
        await command.ExecuteNonQueryAsync(ct);
    }

    private static (string, uint, string, string) TableIdentity(MySqlLocalCommandsQueueWorkerStorageOptions options)
    {
        var connectionString = new MySqlConnectionStringBuilder(options.ConnectionString);
        // `db`.`table`, db.table and table with Database=db lead to the same table
        var tableName = MySqlTableName.Split(options.TableName);
        return (connectionString.Server.ToLowerInvariant(), connectionString.Port,
            tableName.Length > 1 ? tableName[0] : connectionString.Database, tableName[^1]);
    }

    public override bool Equals(object? obj)
    {
        return obj is MySqlLocalCommandsQueueWorkerStorage other && _table == other._table;
    }

    public override int GetHashCode()
    {
        return _table.GetHashCode();
    }

    public string DataSourceName => $"{_table.Database}.{_table.Table}";
}