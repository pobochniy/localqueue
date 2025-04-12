using System.Text.Json;

namespace LocalQueue.Serialization;

/// <summary>
/// Serializer to get json representation of commands. 
/// </summary>
internal class JsonCommandSerializer : ICommandSerializer, ICommandDeserializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Instantiate an instance of serializer to get json representation of commands. 
    /// </summary>
    public JsonCommandSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string Serialize<TCommand>(TCommand command)
    {
        return JsonSerializer.Serialize(command, command!.GetType(), _options);
    }

    /// <inheritdoc />
    public TCommand? Deserialize<TCommand>(string command)
    {
        return JsonSerializer.Deserialize<TCommand>(command, _options);
    }
}