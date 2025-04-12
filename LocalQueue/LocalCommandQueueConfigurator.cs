using System.Text.Json;
using LocalQueue.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace LocalQueue;

/// <summary>
/// Local command queue configurator
/// </summary>
public class LocalCommandQueueConfigurator
{
    private readonly JsonSerializerOptions _serializerOptions = new();

    /// <summary>
    /// Options to control the behavior during serialization/deserialization.
    /// </summary>
    /// <param name="configure"></param>
    public void ConfigureSerializer(Action<JsonSerializerOptions> configure)
    {
        CommandSerializersConfiguration.ConfigureSerializer(_serializerOptions, configure);
    }

    internal void RegisterSerializers(IServiceCollection serviceCollection)
    {
        CommandSerializersConfiguration.RegisterSerializers(_serializerOptions, serviceCollection);
    }
}