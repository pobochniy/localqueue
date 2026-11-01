using Microsoft.Extensions.Configuration;

namespace LocalQueue.PostgreSql;

/// <summary>
/// Methods to register PostgreSQL local command storage in DI container.
/// </summary>
public static class RegistrationExtensions
{

    /// <summary>
    /// Configure local commands queue worker to use PostgreSQL storage
    /// </summary>
    /// <param name="configurator">Local commands queue worker configurator.</param>
    /// <param name="options">Storage options, <see cref="PostgreSqlLocalCommandsQueueWorkerStorageOptions"/>.</param>
    public static void UsePostgreSqlStorage(this LocalCommandQueueWorkerConfigurator configurator, PostgreSqlLocalCommandsQueueWorkerStorageOptions options)
    {
        configurator.UseStorage(new PostgreSqlLocalCommandsQueueWorkerStorage(options));
    }
    
    /// <summary>
    /// Configure local commands queue worker to use PostgreSQL storage
    /// </summary>
    /// <param name="configurator">Local commands queue worker configurator.</param>
    /// <param name="configure">Action to configure storage options, <see cref="PostgreSqlLocalCommandsQueueWorkerStorageOptions"/>.</param>
    public static void UsePostgreSqlStorage(this LocalCommandQueueWorkerConfigurator configurator, Action<PostgreSqlLocalCommandsQueueWorkerStorageOptions> configure)
    {
        var options = new PostgreSqlLocalCommandsQueueWorkerStorageOptions();
        configure.Invoke(options);
        UsePostgreSqlStorage(configurator, options);
    }

    
    /// <summary>
    /// Configure local commands queue worker to use PostgreSQL storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="configuration">Configuration section for command storage options <see cref="PostgreSqlLocalCommandsQueueWorkerStorageOptions"/></param>
    /// <param name="configure">Action to configure storage options.</param>
    public static void UsePostgreSqlStorage(this LocalCommandQueueWorkerConfigurator configurator,
        IConfiguration configuration, Action<PostgreSqlLocalCommandsQueueWorkerStorageOptions>? configure = null)
    {
        var options = new PostgreSqlLocalCommandsQueueWorkerStorageOptions();
        configuration.Bind(options);
        configure?.Invoke(options);
        UsePostgreSqlStorage(configurator, options);
    }
    
    /// <summary>
    /// Configure local commands queue worker to use PostgreSQL storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="options">Storage options, <see cref="PostgreSqlCommandQueueStorageOptions"/>.</param>
    public static void UsePostgreSqlStorage(this LocalCommandQueueConfigurator configurator, PostgreSqlCommandQueueStorageOptions options)
    {
        configurator.UseStorage(sc => new PostgreSqlLocalCommandsQueueStorage(options));
    }
    
    /// <summary>
    /// Configure local commands queue worker to use PostgreSQL storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="configure">Action to configure storage options, <see cref="PostgreSqlCommandQueueStorageOptions"/>.</param>
    public static void UsePostgreSqlStorage(this LocalCommandQueueConfigurator configurator, Action<PostgreSqlCommandQueueStorageOptions> configure)
    {
        var options = new PostgreSqlCommandQueueStorageOptions();
        configure.Invoke(options);
        UsePostgreSqlStorage(configurator, options);
    }

    
    /// <summary>
    /// Configure local commands queue to use PostgreSQL storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="configuration">Configuration section for command storage options <see cref="PostgreSqlCommandQueueStorageOptions"/></param>
    /// <param name="configure">Action to configure storage options.</param>
    public static void UsePostgreSqlStorage(this LocalCommandQueueConfigurator configurator,
        IConfiguration configuration, Action<PostgreSqlCommandQueueStorageOptions>? configure = null)
    {
        var options = new PostgreSqlCommandQueueStorageOptions();
        configuration.Bind(options);
        configure?.Invoke(options);
        UsePostgreSqlStorage(configurator, options);
    }
}