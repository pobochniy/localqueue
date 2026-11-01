using LocalQueue.Diagnostics;
using LocalQueue.Storage;
using Npgsql;
using NpgsqlTypes;

namespace LocalQueue.PostgreSql;

internal class PostgreSqlLocalCommandsQueueWorkerStorage(PostgreSqlLocalCommandsQueueWorkerStorageOptions options)
    : ILocalCommandsQueueWorkerStorage
{
    private const string DefaultSchema = "public";

    // The table identity: connection strings that differ in key order or pool settings lead to the same table
    private readonly (string Host, int Port, string Database, string Schema, string Table) _table =
        TableIdentity(options);

    public async Task<IEnumerable<CommandRecord>> Prefetch(int count, TimeSpan timeout,
        IReadOnlyCollection<string> commandTypes, CancellationToken ct)
    {
        if (commandTypes.Count == 0)
        {
            throw new ArgumentException("At least one command type must be specified.", nameof(commandTypes));
        }

        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);

        // A lateral subquery per type: with `command_type = any(...)` the index can't give the order,
        // all matching rows get locked. The update runs in the same statement, so no explicit transaction is needed.
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = $@"
            with candidates as (
                select c.id, c.created_at_utc
                from unnest(@commandTypes) as t(command_type)
                cross join lateral (
                    select q.id, q.created_at_utc
                    from {options.TableName} q
                    where q.command_type = t.command_type
                        and (q.locked_till_utc is null or q.locked_till_utc < now())
                    order by q.created_at_utc
                    limit @count
                    for update skip locked
                ) c
                order by c.created_at_utc
                limit @count
            ), updated as (
                update {options.TableName} q
                set locked_till_utc = now() + @timeout, try_count = q.try_count + 1
                from candidates
                where q.id = candidates.id
                returning q.id, q.command_type, q.data::text as data, q.created_at_utc, q.locked_till_utc, q.try_count
            )
            select id, command_type, data, created_at_utc, locked_till_utc, try_count
            from updated
            order by created_at_utc";
        command.Parameters.Add(new NpgsqlParameter<string[]>("commandTypes", commandTypes.ToArray()));
        command.Parameters.Add(new NpgsqlParameter("count", NpgsqlDbType.Integer) { Value = count });
        command.Parameters.Add(new NpgsqlParameter("timeout", NpgsqlDbType.Interval) { Value = timeout });

        var list = new List<CommandRecord>(count);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new CommandRecord
            {
                Id = reader.GetGuid(0),
                CommandType = reader.GetString(1),
                Data = reader.GetString(2),
                CreatedAtUtc = reader.GetDateTime(3),
                LockedTillUtc = reader.GetDateTime(4),
                TryCount = reader.GetInt32(5)
            });
        }

        return list;
    }

    public async Task Delete(Guid id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteNonQueryAsync(
            $"delete from {options.TableName} where id = @id",
            [new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id }],
            ct,
            commandTimeout: options.CommandTimeoutSeconds);
    }

    public async Task<IEnumerable<LocalQueueStorageSummary>> GetSummary(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandTimeout = options.CommandTimeoutSeconds;
        selectCommand.CommandText =
            $@"select command_type, count(*)::int as count, max(try_count) as max_try_count
            from {options.TableName} group by command_type";

        var result = new List<LocalQueueStorageSummary>();
        await using var reader = await selectCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new LocalQueueStorageSummary
            {
                CommandType = reader.GetString(0),
                Count = reader.GetInt32(1),
                MaxTryCount = reader.GetInt32(2)
            });
        }

        return result;
    }

    public async Task MakeInvisibleFor(Guid id, TimeSpan timeout, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteNonQueryAsync(
            $"update {options.TableName} set locked_till_utc = now() + @timeout where id = @id",
            [
                new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id },
                new NpgsqlParameter("timeout", NpgsqlDbType.Interval) { Value = timeout }
            ],
            ct,
            commandTimeout: options.CommandTimeoutSeconds);
    }

    private static (string, int, string, string, string) TableIdentity(
        PostgreSqlLocalCommandsQueueWorkerStorageOptions options)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        // "schema"."table", schema.table and table (in the public schema) lead to the same table
        var tableName = PostgreSqlTableName.Split(options.TableName);
        var database = connectionString.Database ?? connectionString.Username ?? string.Empty;
        return ((connectionString.Host ?? string.Empty).ToLowerInvariant(), connectionString.Port,
            database, tableName.Length > 1 ? tableName[^2] : DefaultSchema, tableName[^1]);
    }

    public override bool Equals(object? obj)
    {
        return obj is PostgreSqlLocalCommandsQueueWorkerStorage other && _table == other._table;
    }

    public override int GetHashCode()
    {
        return _table.GetHashCode();
    }

    public string DataSourceName => $"{_table.Database}.{_table.Schema}.{_table.Table}";
}
