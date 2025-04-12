using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace LocalQueue.Serialization;

internal static class CommandSerializersConfiguration
{
    internal static void ConfigureSerializer(JsonSerializerOptions serializerOptions,
        Action<JsonSerializerOptions> configure)
    {
        serializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        configure(serializerOptions);
    }

    internal static void RegisterSerializers(JsonSerializerOptions serializerOptions,
        IServiceCollection serviceCollection)
    {
        var serializer = new JsonCommandSerializer(serializerOptions);
        serviceCollection.AddSingleton<ICommandSerializer>(serializer);
        serviceCollection.AddSingleton<ICommandDeserializer>(serializer);
    }
}
