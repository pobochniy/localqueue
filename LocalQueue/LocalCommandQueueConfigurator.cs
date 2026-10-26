using System.Text.Json;
using LocalQueue.Queue;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LocalQueue;

/// <summary>
/// Local command queue configurator
/// </summary>
public class LocalCommandQueueConfigurator
{
    private readonly JsonSerializerOptions _serializerOptions = new();
    private Func<IServiceProvider, ILocalCommandsQueueStorage>? _storageFactory;

    /// <summary>
    /// Options to control the behavior during serialization/deserialization.
    /// </summary>
    /// <param name="configure"></param>
    public void ConfigureSerializer(Action<JsonSerializerOptions> configure)
    {
        CommandSerializersConfiguration.ConfigureSerializer(_serializerOptions, configure);
    }
    
    /// <summary>
    /// Add storage registration
    /// </summary>
    /// <param name="storage">The instance of storage.</param>
    public void UseStorage(ILocalCommandsQueueStorage storage)
    {
        _storageFactory = _ => storage;
    }
    
    /// <summary>
    /// Add storage registration
    /// </summary>
    /// <param name="storageFactory">The factory which creates the storage instance.</param>
    public void UseStorage(Func<IServiceProvider, ILocalCommandsQueueStorage> storageFactory)
    {
        _storageFactory = storageFactory;
    }

    internal void RegisterCommandQueue(IServiceCollection serviceCollection)
    {
        serviceCollection.TryAddSingleton<CommandsQueue>(sp => new CommandsQueue(_storageFactory!(sp), new JsonCommandSerializer(_serializerOptions)));
        serviceCollection.TryAddSingleton<ICommandsQueue>(sp => sp.GetRequiredService<CommandsQueue>());
    }
}