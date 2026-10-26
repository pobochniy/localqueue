using LocalQueue.Queue;
using LocalQueue.Serialization;
using LocalQueue.Storage;
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
    /// Adds local command queue. <see cref="ILocalCommandsQueueStorage"/> must be registered in DI./>
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">Action to configure command processing options. </param>
    public static IServiceCollection AddLocalCommandQueue(this IServiceCollection serviceCollection,
        Action<LocalCommandQueueConfigurator>? configure = null)
    {
        var configurator = new LocalCommandQueueConfigurator();
        configure?.Invoke(configurator);
        configurator.RegisterCommandQueue(serviceCollection);
        return serviceCollection;
    }

    /// <summary>
    /// Adds local command handlers. <see cref="ILocalCommandsQueueWorkerStorage"/> must be registered in DI.
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
    /// Adds local command handlers. <see cref="ILocalCommandsQueueWorkerStorage"/> must be registered in DI.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">Action to configure command processing options. </param>
    public static IServiceCollection AddLocalCommandQueueWorker(this IServiceCollection serviceCollection,
        Action<LocalCommandQueueWorkerConfigurator> configure)
    {
        return serviceCollection.AddLocalCommandQueueWorker(new LocalCommandQueueWorkerConfigurator(), configure);
    }

    internal static IServiceCollection AddLocalCommandQueueWorker(this IServiceCollection serviceCollection,
        LocalCommandQueueWorkerConfigurator options,
        Action<LocalCommandQueueWorkerConfigurator>? configure = null)
    {
        configure?.Invoke(options);
        options.RegisterWorker(serviceCollection);
        return serviceCollection;
    }
}