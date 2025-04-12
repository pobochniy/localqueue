namespace LocalQueue.Serialization;

/// <summary>
/// Interface for command serializer.
/// </summary>
internal interface ICommandSerializer
{
    /// <summary>
    /// Serialize an instance of command type.
    /// </summary>
    /// <param name="command">The command to serialize.</param>
    /// <returns>Event serialized as string.</returns>
    string Serialize<TCommand>(TCommand command);
}