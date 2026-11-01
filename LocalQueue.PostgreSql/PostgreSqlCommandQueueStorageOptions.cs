namespace LocalQueue.PostgreSql;

/// <summary>
/// Options to setup PostgreSQL local commands storage.
/// </summary>
public class PostgreSqlCommandQueueStorageOptions
{
    /// <summary>
    /// Local commands storage table name, optionally qualified by the schema.
    /// </summary>
    /// <remarks>Default is localcommandsqueue.</remarks>
    public string TableName { get; set; } = "localcommandsqueue";

    /// <summary>
    /// Maximum number of commands to insert in a single request.
    /// </summary>
    /// <remarks> Default is 100 commands.</remarks>
    public int InsertBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time before terminating the attempt to execute a PostgreSQL command and generating an error.
    /// </summary>
    /// <remarks> Default is 5 seconds.</remarks>
    public int CommandTimeoutSeconds { get; set; } = 5;
}
