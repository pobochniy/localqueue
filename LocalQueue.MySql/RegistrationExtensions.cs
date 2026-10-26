using Microsoft.Extensions.Configuration;

namespace LocalQueue.MySql;

/// <summary>
/// Methods to register MySql local command storage in DI container.
/// </summary>
public static class RegistrationExtensions
{

    /// <summary>
    /// Configure local commands queue worker to use mysql storage
    /// </summary>
    /// <param name="configurator">Local commands queue worker configurator.</param>
    /// <param name="options">Storage options, <see cref="MySqlLocalCommandsQueueWorkerStorageOptions"/>.</param>
    public static void UseMySqlStorage(this LocalCommandQueueWorkerConfigurator configurator, MySqlLocalCommandsQueueWorkerStorageOptions options)
    {
        configurator.UseStorage(new MySqlLocalCommandsQueueWorkerStorage(options));
    }
    
    /// <summary>
    /// Configure local commands queue worker to use mysql storage
    /// </summary>
    /// <param name="configurator">Local commands queue worker configurator.</param>
    /// <param name="configure">Action to configure storage options, <see cref="MySqlLocalCommandsQueueWorkerStorageOptions"/>.</param>
    public static void UseMySqlStorage(this LocalCommandQueueWorkerConfigurator configurator, Action<MySqlLocalCommandsQueueWorkerStorageOptions> configure)
    {
        var options = new MySqlLocalCommandsQueueWorkerStorageOptions();
        configure.Invoke(options);
        UseMySqlStorage(configurator, options);
    }

    
    /// <summary>
    /// Configure local commands queue worker to use mysql storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="configuration">Configuration section for command storage options <see cref="MySqlLocalCommandsQueueWorkerStorageOptions"/></param>
    /// <param name="configure">Action to configure storage options.</param>
    public static void UseMySqlStorage(this LocalCommandQueueWorkerConfigurator configurator,
        IConfiguration configuration, Action<MySqlLocalCommandsQueueWorkerStorageOptions>? configure = null)
    {
        var options = new MySqlLocalCommandsQueueWorkerStorageOptions();
        configuration.Bind(options);
        configure?.Invoke(options);
        UseMySqlStorage(configurator, options);
    }
    
    /// <summary>
    /// Configure local commands queue worker to use mysql storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="options">Storage options, <see cref="MySqlCommandQueueStorageOptions"/>.</param>
    public static void UseMySqlStorage(this LocalCommandQueueConfigurator configurator, MySqlCommandQueueStorageOptions options)
    {
        configurator.UseStorage(sc => new MySqlLocalCommandsQueueStorage(options));
    }
    
    /// <summary>
    /// Configure local commands queue worker to use mysql storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="configure">Action to configure storage options, <see cref="MySqlCommandQueueStorageOptions"/>.</param>
    public static void UseMySqlStorage(this LocalCommandQueueConfigurator configurator, Action<MySqlCommandQueueStorageOptions> configure)
    {
        var options = new MySqlCommandQueueStorageOptions();
        configure.Invoke(options);
        UseMySqlStorage(configurator, options);
    }

    
    /// <summary>
    /// Configure local commands queue to use mysql storage
    /// </summary>
    /// <param name="configurator">Local commands queue configurator.</param>
    /// <param name="configuration">Configuration section for command storage options <see cref="MySqlCommandQueueStorageOptions"/></param>
    /// <param name="configure">Action to configure storage options.</param>
    public static void UseMySqlStorage(this LocalCommandQueueConfigurator configurator,
        IConfiguration configuration, Action<MySqlCommandQueueStorageOptions>? configure = null)
    {
        var options = new MySqlCommandQueueStorageOptions();
        configuration.Bind(options);
        configure?.Invoke(options);
        UseMySqlStorage(configurator, options);
    }
}