using LocalQueue.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LocalQueue;

/// <summary>
/// Methods to register command handlers in DI container
/// </summary>
public static class ServiceCollectionExtension
{
    /// <summary>
    /// Adds local command queue.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">Action to configure command processing options. </param>
    public static IServiceCollection AddLocalCommandQueue(this IServiceCollection serviceCollection,
        Action<LocalCommandQueueConfigurator>? configure = null)
    {
        var configurator = new LocalCommandQueueConfigurator();
        configure?.Invoke(configurator);
        configurator.RegisterSerializers(serviceCollection);

        serviceCollection.TryAddSingleton<CommandsQueue>();
        serviceCollection.TryAddSingleton<ICommandsQueue>(sp => sp.GetRequiredService<CommandsQueue>());

        return serviceCollection;
    }

    /// <summary>
    /// Adds local command handlers.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configuration">Configuration section command processing options <see cref="LocalCommandQueueWorkerConfigurator"/></param>
    /// <param name="configure">Action to configure command processing options. </param>
    public static IServiceCollection AddLocalCommandQueueWorker(this IServiceCollection serviceCollection,
        IConfiguration configuration,
        Action<LocalCommandQueueWorkerConfigurator> configure)
    {
        var configurator = new LocalCommandQueueWorkerConfigurator();
        configuration.Bind(configurator);
        return serviceCollection.AddLocalCommandQueueWorker(configurator, configure);
    }

    /// <summary>
    /// Adds local command handlers.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">Action to configure command processing options. </param>
    public static IServiceCollection AddLocalCommandQueueWorker(this IServiceCollection serviceCollection,
        Action<LocalCommandQueueWorkerConfigurator> configure)
    {
        return serviceCollection.AddLocalCommandQueueWorker(new LocalCommandQueueWorkerConfigurator(), configure);
    }

    /// <summary>
    /// Adds local command handlers.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="options">Already configured <see cref="LocalCommandQueueWorkerConfigurator"/></param>
    /// <param name="configure">Action to configure command processing options. </param>
    public static IServiceCollection AddLocalCommandQueueWorker(this IServiceCollection serviceCollection,
        LocalCommandQueueWorkerConfigurator options,
        Action<LocalCommandQueueWorkerConfigurator>? configure = null)
    {
        configure?.Invoke(options);
        options.RegisterHandlers(serviceCollection);
        options.RegisterSerializers(serviceCollection);
        return serviceCollection;
    }
}