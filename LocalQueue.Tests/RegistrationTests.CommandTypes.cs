using LocalQueue.Processing;
using LocalQueue.RetryPolicy;
using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LocalQueue.Tests;

public partial class RegistrationTests
{
    [CommandType("send-mail", "Old.SendMailCommand")]
    private class SendMailCommand;

    [CommandType("send-mail")]
    private class OtherSendMailCommand;

    [CommandType("Old.SendMailCommand")]
    private class ReusesPreviousNameCommand;

    private class NamedCommandHandler<TCommand> : ICommandHandler<TCommand>
    {
        public Task Handle(TCommand command, CancellationToken ct) => Task.CompletedTask;
    }

    [Test]
    public async Task WorkerFetchesCommandsByNamePreviousNamesAndFullName()
    {
        var storage = new PrefetchRecordingStorage();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(storage.Storage)
            .AddCommandHandler<SendMailCommand, NamedCommandHandler<SendMailCommand>>());

        var serviceProvider = serviceCollection.BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        var fetching = serviceProvider.GetRequiredService<CommandFetcher>().Run(cts.Token);

        var commandTypes = await storage.FetchedCommandTypes.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => fetching);

        Assert.That(commandTypes, Is.EquivalentTo(new[]
        {
            "send-mail", "Old.SendMailCommand", typeof(SendMailCommand).FullName
        }));
        serviceProvider.AssertServiceHasCommandType<SendMailCommand>();
    }

    [Test]
    public void CannotRegisterTwoCommandTypesUnderTheSameName()
    {
        var serviceCollection = new ServiceCollection();

        var e = Assert.Throws<InvalidOperationException>(() => serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<SendMailCommand, NamedCommandHandler<SendMailCommand>>()
            .AddCommandHandler<OtherSendMailCommand, NamedCommandHandler<OtherSendMailCommand>>()));
        Assert.That(e!.Message, Does.Contain("'send-mail'"));
    }

    [Test]
    public void CannotRegisterCommandTypeUnderPreviousNameOfAnother()
    {
        var serviceCollection = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<SendMailCommand, NamedCommandHandler<SendMailCommand>>()
            .AddCommandHandler<ReusesPreviousNameCommand, NamedCommandHandler<ReusesPreviousNameCommand>>()));
    }

    [Test]
    public void CanRegisterTheSameCommandTypeAgain()
    {
        var serviceCollection = new ServiceCollection();

        Assert.DoesNotThrow(() => serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<SendMailCommand, NamedCommandHandler<SendMailCommand>>()
            .AddCommandHandler<SendMailCommand, NamedCommandHandler<SendMailCommand>>()));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CannotRegisterRetryPolicyWithoutAttempts(int maxAttempts)
    {
        var serviceCollection = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => serviceCollection.AddLocalCommandQueueWorker(c => c
            .UseStorage(Mock.Of<ILocalCommandsQueueWorkerStorage>())
            .AddCommandHandler<SendMailCommand, NamedCommandHandler<SendMailCommand>>(
                new RetryPolicyOptions { MaxAttempts = maxAttempts })));
    }
}
