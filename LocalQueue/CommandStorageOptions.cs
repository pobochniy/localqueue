namespace LocalQueue;

/// <summary>
/// Options to setup local commands storage.
/// </summary>
public class CommandStorageOptions
{
    /// <summary>
    /// Connection string used for local commands storage.
    /// </summary>
    public string ConnectionString { get; set; } = null!;

    /// <summary>
    /// Local commands storage table name.
    /// </summary>
    /// <remarks>Default is localcommandsqueue.</remarks>
    public string TableName { get; set; } = "localcommandsqueue";

    /// <summary>
    /// Maximum number of commands to insert in a single request.
    /// </summary>
    /// <remarks> Default is 100 commands.</remarks>
    public int InsertBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time before terminating the attempt to execute a command and generating an error.
    /// </summary>
    /// <remarks> Default is 5 seconds.</remarks>
    public int CommandTimeoutSeconds { get; set; } = 5;
}
