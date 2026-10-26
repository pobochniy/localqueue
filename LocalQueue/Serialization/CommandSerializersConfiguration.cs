using System.Text.Encodings.Web;
using System.Text.Json;

namespace LocalQueue.Serialization;

internal static class CommandSerializersConfiguration
{
    internal static void ConfigureSerializer(JsonSerializerOptions serializerOptions,
        Action<JsonSerializerOptions> configure)
    {
        serializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        configure(serializerOptions);
    }
}
