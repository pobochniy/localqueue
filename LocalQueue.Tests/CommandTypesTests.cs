using System.Data;
using System.Text.Json;
using LocalQueue.Queue;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using Moq;

namespace LocalQueue.Tests;

public class CommandTypesTests
{
    private class PlainCommand;

    [CommandType("named")]
    private class NamedCommand;

    [CommandType("renamed", "named-before", "named-long-ago")]
    private class RenamedCommand;

    [CommandType(" ")]
    private class EmptyNameCommand;

    [CommandType("valid", "")]
    private class EmptyPreviousNameCommand;

    private class DerivedFromNamedCommand : NamedCommand;

    [Test]
    public void ShouldUseFullName_WithoutAttribute()
    {
        Assert.That(CommandTypes.NameOf<PlainCommand>(), Is.EqualTo(typeof(PlainCommand).FullName));
        Assert.That(CommandTypes.Of(typeof(PlainCommand)).Accepted, Is.EquivalentTo(new[] { typeof(PlainCommand).FullName }));
    }

    [Test]
    public void ShouldUseAttributeName()
    {
        Assert.That(CommandTypes.NameOf<NamedCommand>(), Is.EqualTo("named"));
    }

    [Test]
    public void ShouldAcceptPreviousNamesAndFullName()
    {
        Assert.That(CommandTypes.Of(typeof(RenamedCommand)).Accepted, Is.EquivalentTo(new[]
        {
            "renamed", "named-before", "named-long-ago", typeof(RenamedCommand).FullName
        }));
    }

    [Test]
    public void ShouldNotInheritName()
    {
        // A derived command is a command type of its own: it must not be handled as its base
        Assert.That(CommandTypes.NameOf<DerivedFromNamedCommand>(), Is.EqualTo(typeof(DerivedFromNamedCommand).FullName));
    }

    [Test]
    public void ShouldRejectEmptyNames()
    {
        Assert.Throws<ArgumentException>(() => CommandTypes.NameOf<EmptyNameCommand>());
        Assert.Throws<ArgumentException>(() => CommandTypes.NameOf<EmptyPreviousNameCommand>());
    }

    [Test]
    public void ShouldRejectNameLongerThanTheColumn()
    {
        var attribute = typeof(TooLongNameCommand).GetCustomAttributes(false).OfType<CommandTypeAttribute>().Single();
        Assert.That(attribute.Name, Has.Length.GreaterThan(CommandTypes.MaxNameLength));
        Assert.Throws<ArgumentException>(() => CommandTypes.NameOf<TooLongNameCommand>());
    }

    [CommandType("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789"
                 + "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789"
                 + "01234567890123456789012345678901234567890123456789X")]
    private class TooLongNameCommand;

    [Test]
    public async Task ShouldEnqueueUnderTheName()
    {
        var records = new List<CommandRecord>();
        var storage = new Mock<ILocalCommandsQueueStorage>();
        storage
            .Setup(x => x.Create(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<IEnumerable<CommandRecord>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IDbConnection, IDbTransaction, IEnumerable<CommandRecord>, CancellationToken>((_, _, c, _) => records.AddRange(c))
            .Returns(Task.CompletedTask);
        var queue = new CommandsQueue(storage.Object, new JsonCommandSerializer(new JsonSerializerOptions()));

        await queue.Enqueue(Mock.Of<IDbConnection>(), Mock.Of<IDbTransaction>(),
            new object[] { new RenamedCommand(), new PlainCommand() }, CancellationToken.None);

        Assert.That(records.Select(x => x.CommandType), Is.EqualTo(new[] { "renamed", typeof(PlainCommand).FullName }));
    }
}
