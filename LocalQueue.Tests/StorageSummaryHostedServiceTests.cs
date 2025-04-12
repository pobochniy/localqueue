using System.Reflection;
using System.Text.Json;
using LocalQueue.Processing;
using LocalQueue.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LocalQueue.Tests;

[TestFixture]
public class StorageSummaryHostedServiceTests
{
    [Test]
    public void ShouldHaveCommandTypesForMetrics()
    {
        var service = new StorageSummaryHostedService(
            new Mock<IServiceScopeFactory>().Object,
            new IRawCommandHandler[]
            {
                CreateRawHandler<FirstLocalCommand>(),
                CreateRawHandler<SecondLocalCommand>()
            },
            NullLogger<StorageSummaryHostedService>.Instance);

        AssertServiceHasCommandType<FirstLocalCommand>(service);
        AssertServiceHasCommandType<SecondLocalCommand>(service);
    }

    private static void AssertServiceHasCommandType<TCommand>(StorageSummaryHostedService service)
    {
        var commandTypeField = service
            .GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(IReadOnlyCollection<string>));

        var commandTypes = (IReadOnlyCollection<string>)commandTypeField.GetValue(service)!;
        var commandType = commandTypes.FirstOrDefault(t => t == typeof(TCommand).FullName!);
        Assert.That(commandType, Is.Not.Null);
    }

    private static RawCommandHandler<TCommand> CreateRawHandler<TCommand>()
    {
        return new RawCommandHandler<TCommand>(
            new CommandProcessingOptions(),
            new Mock<IServiceScopeFactory>().Object,
            new JsonCommandSerializer(new JsonSerializerOptions()),
            NullLogger<RawCommandHandler<TCommand>>.Instance);
    }

    private class FirstLocalCommand
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private class SecondLocalCommand
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}