namespace LocalQueue.PostgreSql;

/// <summary>
/// Options to setup PostgreSQL local commands worker storage.
/// </summary>
public class PostgreSqlLocalCommandsQueueWorkerStorageOptions
{
    /// <summary>
    /// Connection string to PostgreSQL used for local commands storage.
    /// </summary>
    public string ConnectionString { get; set; } = null!;

    /// <summary>
    /// Local commands storage table name, optionally qualified by the schema.
    /// </summary>
    /// <remarks>Default is localcommandsqueue.</remarks>
    public string TableName { get; set; } = "localcommandsqueue";

    /// <summary>
    /// Maximum time before terminating the attempt to execute a PostgreSQL command and generating an error.
    /// </summary>
    /// <remarks> Default is 5 seconds.</remarks>
    public int CommandTimeoutSeconds { get; set; } = 5;
}
