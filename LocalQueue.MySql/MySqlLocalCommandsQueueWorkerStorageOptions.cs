namespace LocalQueue.MySql;

/// <summary>
/// Options to setup MySql local commands storage.
/// </summary>
public class MySqlLocalCommandsQueueWorkerStorageOptions
{
    /// <summary>
    /// Connection string to MySQL used for local commands storage.
    /// </summary>
    public string ConnectionString { get; set; } = null!;

    /// <summary>
    /// Local commands storage table name.
    /// </summary>
    /// <remarks>Default is localcommandsqueue.</remarks>
    public string TableName { get; set; } = "localcommandsqueue";

    /// <summary>
    /// Maximum time before terminating the attempt to execute a MySql command and generating an error.
    /// </summary>
    /// <remarks> Default is 5 seconds.</remarks>
    public int CommandTimeoutSeconds { get; set; } = 5;
}