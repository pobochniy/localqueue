// using LocalQueue.Storage;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.DependencyInjection.Extensions;
//
// namespace LocalQueue.PostgreSql;
//
// /// <summary>
// /// Methods to register MySql local command storage in DI container.
// /// </summary>
// public static class RegistrationExtensions
// {
//     /// <summary>
//     /// Add MySql <see cref="ICommandsStorage"/>.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="configuration">Configuration section command processing options <see cref="CommandStorageOptions"/></param>
//     /// <param name="configure">Action to configure MySql local commands storage options.</param>
//     public static IServiceCollection AddMySqlCommandsStorage(this IServiceCollection serviceCollection,
//         IConfiguration configuration,
//         Action<IServiceProvider, CommandStorageOptions>? configure = null)
//     {
//         var configurator = new CommandStorageOptions();
//         configuration.Bind(configurator);
//         return serviceCollection.AddMySqlCommandsStorage(configurator, configure);
//     }
//     
//     /// <summary>
//     /// Add MySql <see cref="ICommandsStorage"/>.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="configure">Action to configure MySql local commands storage options.</param>
//     public static IServiceCollection AddMySqlCommandsStorage(this IServiceCollection serviceCollection,
//         Action<IServiceProvider, CommandStorageOptions> configure)
//     {
//         return serviceCollection.AddMySqlCommandsStorage(new CommandStorageOptions(), configure);
//     }
//
//     /// <summary>
//     /// Add MySql <see cref="ICommandsStorage"/>.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="options">MySql local commands storage options.</param>
//     /// <param name="configure">Action to configure MySql local commands storage options.</param>
//     public static IServiceCollection AddMySqlCommandsStorage(this IServiceCollection serviceCollection,
//         CommandStorageOptions options,
//         Action<IServiceProvider, CommandStorageOptions>? configure = null)
//     {
//         serviceCollection.TryAddSingleton<ICommandsStorage>(
//             sp =>
//             {
//                 configure?.Invoke(sp, options);
//                 return new CommandStorage(options);
//             });
//         return serviceCollection;
//     }
//
//     /// <summary>
//     /// Adds MySql local command queue.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="configuration">Configuration section command processing options <see cref="CommandStorageOptions"/> and <see cref="LocalCommandQueueWorkerConfigurator"/></param>
//     /// <param name="storageConfigure">Action to configure MySql local commands storage options.</param>
//     /// <param name="queueConfigure">Action to configure command processing options.</param>
//     public static IServiceCollection AddMySqlCommandQueue(this IServiceCollection serviceCollection,
//         IConfiguration configuration,
//         Action<IServiceProvider, CommandStorageOptions>? storageConfigure = null,
//         Action<LocalCommandQueueConfigurator>? queueConfigure = null)
//     {
//         serviceCollection.AddMySqlCommandsStorage(configuration, storageConfigure);
//         serviceCollection.AddLocalCommandQueue(queueConfigure);
//
//         return serviceCollection;
//     }
//
//     /// <summary>
//     /// Adds MySql local command queue.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="storageConfigure">Action to configure MySql local commands storage options.</param>
//     /// <param name="queueConfigure">Action to configure command processing options.</param>
//     public static IServiceCollection AddMySqlCommandQueue(this IServiceCollection serviceCollection,
//         Action<IServiceProvider, CommandStorageOptions> storageConfigure,
//         Action<LocalCommandQueueConfigurator>? queueConfigure = null)
//     {
//         serviceCollection.AddMySqlCommandsStorage(storageConfigure);
//         serviceCollection.AddLocalCommandQueue(queueConfigure);
//
//         return serviceCollection;
//     }
//
//     /// <summary>
//     /// Adds MySql local command queue.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="storageOptions">MySql local commands storage options.</param>
//     /// <param name="storageConfigure">Action to configure MySql local commands storage options.</param>
//     /// <param name="queueConfigure">Action to configure command processing options.</param>
//     public static IServiceCollection AddMySqlCommandQueue(this IServiceCollection serviceCollection,
//         CommandStorageOptions storageOptions,
//         Action<IServiceProvider, CommandStorageOptions>? storageConfigure = null,
//         Action<LocalCommandQueueConfigurator>? queueConfigure = null)
//     {
//         serviceCollection.AddMySqlCommandsStorage(storageOptions, storageConfigure);
//         serviceCollection.AddLocalCommandQueue(queueConfigure);
//
//         return serviceCollection;
//     }
//
//     /// <summary>
//     /// Adds MySql local command handlers.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="configuration">Configuration section command processing options <see cref="CommandStorageOptions"/> and <see cref="LocalCommandQueueWorkerConfigurator"/></param>
//     /// <param name="storageConfigure">Action to configure MySql local commands storage options.</param>
//     /// <param name="workerConfigure">Action to configure command processing options.</param>
//     public static IServiceCollection AddMySqlCommandQueueWorker(this IServiceCollection serviceCollection,
//         IConfiguration configuration,
//         Action<LocalCommandQueueWorkerConfigurator> workerConfigure,
//         Action<IServiceProvider, CommandStorageOptions>? storageConfigure = null)
//     {
//         serviceCollection.AddMySqlCommandsStorage(configuration, storageConfigure);
//         serviceCollection.AddLocalCommandQueueWorker(configuration, workerConfigure);
//
//         return serviceCollection;
//     }
//
//     /// <summary>
//     /// Adds MySql local command handlers.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="storageConfigure">Action to configure MySql local commands storage options.</param>
//     /// <param name="workerConfigure">Action to configure command processing options.</param>
//     public static IServiceCollection AddMySqlCommandQueueWorker(this IServiceCollection serviceCollection,
//         Action<IServiceProvider, CommandStorageOptions> storageConfigure,
//         Action<LocalCommandQueueWorkerConfigurator> workerConfigure)
//     {
//         serviceCollection.AddMySqlCommandsStorage(storageConfigure);
//         serviceCollection.AddLocalCommandQueueWorker(workerConfigure);
//
//         return serviceCollection;
//     }
//
//     /// <summary>
//     /// Adds MySql local command handlers.
//     /// </summary>
//     /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the services to.</param>
//     /// <param name="storageOptions">MySql local commands storage options.</param>
//     /// <param name="workerOptions">Already configured <see cref="LocalCommandQueueWorkerConfigurator"/></param>
//     /// <param name="storageConfigure">Action to configure MySql local commands storage options.</param>
//     /// <param name="workerConfigure">Action to configure command processing options.</param>
//     public static IServiceCollection AddMySqlCommandQueueWorker(this IServiceCollection serviceCollection,
//         CommandStorageOptions? storageOptions = null,
//         LocalCommandQueueWorkerConfigurator? workerOptions = null,
//         Action<LocalCommandQueueWorkerConfigurator>? workerConfigure = null,
//         Action<IServiceProvider, CommandStorageOptions>? storageConfigure = null)
//     {
//         serviceCollection.AddMySqlCommandsStorage(storageOptions ?? new CommandStorageOptions(), storageConfigure);
//         serviceCollection.AddLocalCommandQueueWorker(workerOptions ?? new LocalCommandQueueWorkerConfigurator(), workerConfigure);
//
//         return serviceCollection;
//     }
// }