namespace LocalQueue;

/// <summary>
/// The name the command type is stored under, instead of <see cref="Type.FullName"/> of its class.
/// </summary>
/// <remarks>
/// The command row keeps the type name, and the worker finds the handler by it. Without the attribute it is
/// the full name of the class, so renaming the class or moving it to another namespace leaves the commands
/// already in the table without a handler. With the attribute the class can be renamed and moved freely.
/// <para>
/// The worker also takes the commands stored under <paramref name="previousNames"/> and under the full name of
/// the class, so the attribute may be added to a command that is already in use, and the name may be changed
/// later with the old one listed in <paramref name="previousNames"/>. New commands are stored under
/// <paramref name="name"/> only.
/// </para>
/// </remarks>
/// <param name="name">Stable name of the command type, at most 250 characters.</param>
/// <param name="previousNames">Names the command type was stored under before.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CommandTypeAttribute(string name, params string[] previousNames) : Attribute
{
    /// <summary>
    /// Stable name of the command type.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Names the command type was stored under before.
    /// </summary>
    public IReadOnlyList<string> PreviousNames { get; } = previousNames;
}
