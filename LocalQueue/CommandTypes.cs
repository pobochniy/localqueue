using System.Collections.Concurrent;
using System.Reflection;

namespace LocalQueue;

/// <summary>
/// The names command types are stored under.
/// </summary>
public static class CommandTypes
{
    /// <summary>
    /// The longest name the storages keep: the <c>command_type</c> column is <c>varchar(250)</c>.
    /// </summary>
    public const int MaxNameLength = 250;

    private static readonly ConcurrentDictionary<Type, CommandTypeNames> Names = new();

    /// <summary>
    /// The name <typeparamref name="TCommand"/> is stored under: <see cref="CommandTypeAttribute.Name"/>, or the
    /// full name of the class without the attribute.
    /// </summary>
    /// <remarks>
    /// For the code that writes the command rows itself, e.g. through an ORM in the transaction of its own changes.
    /// </remarks>
    public static string NameOf<TCommand>() => NameOf(typeof(TCommand));

    /// <inheritdoc cref="NameOf{TCommand}"/>
    /// <param name="commandType">Type of the command.</param>
    public static string NameOf(Type commandType) => Of(commandType).Name;

    internal static CommandTypeNames Of(Type commandType) => Names.GetOrAdd(commandType, Describe);

    private static CommandTypeNames Describe(Type commandType)
    {
        var fullName = commandType.FullName
                       ?? throw new ArgumentException($"Command type {commandType} has no full name.", nameof(commandType));
        var attribute = commandType.GetCustomAttribute<CommandTypeAttribute>(inherit: false);
        if (attribute == null)
        {
            return new CommandTypeNames(fullName, [fullName]);
        }

        Validate(commandType, attribute.Name);
        foreach (var previousName in attribute.PreviousNames)
        {
            Validate(commandType, previousName);
        }

        var accepted = new[] { attribute.Name }
            .Concat(attribute.PreviousNames)
            .Append(fullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new CommandTypeNames(attribute.Name, accepted);
    }

    private static void Validate(Type commandType, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"Command type name of {commandType.FullName} must not be empty.",
                nameof(commandType));
        }

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Command type name '{name}' of {commandType.FullName} is longer than {MaxNameLength} characters.",
                nameof(commandType));
        }
    }
}

/// <summary>
/// The name new commands are stored under and all the names the worker takes the commands of the type by.
/// </summary>
/// <param name="Name">The name new commands are stored under; the metrics and the retry policy are keyed by it.</param>
/// <param name="Accepted">The name, the previous names and the full name of the class.</param>
internal sealed record CommandTypeNames(string Name, IReadOnlyCollection<string> Accepted);
