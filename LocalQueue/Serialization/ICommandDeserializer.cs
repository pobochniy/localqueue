namespace LocalQueue.Serialization;

/// <summary>
/// Interface for command deserializer.
/// </summary>
internal interface ICommandDeserializer
{
    /// <summary>
    /// Deserialize an object of type TCommand from a string.
    /// </summary>
    /// <param name="command">String data to deserialize.</param>
    /// <returns>Deserialized command of type TCommand.</returns>
    TCommand? Deserialize<TCommand>(string command);
}