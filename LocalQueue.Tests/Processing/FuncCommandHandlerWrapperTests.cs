using LocalQueue.Processing;
using LocalQueue.Tests.Fakes;
using Moq;

namespace LocalQueue.Tests.Processing;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class FuncCommandHandlerWrapperTests
{
    private readonly ServiceScopeFactoryFake _scopeFactory = new();

    // ReSharper disable once ClassNeverInstantiated.Local
    // ReSharper disable once UnusedMember.Local
    private record TestCommand
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    [Test]
    public async Task ShouldInvokeHandlerWithPassedArguments()
    {
        using var cts = new CancellationTokenSource();
        var command = new TestCommand();

        IServiceProvider? actualServiceProvider = null;
        TestCommand? actualCommandRecord = null;
        CancellationToken? actualCancellationToken = null;

        var sut = new FuncCommandHandlerWrapper<TestCommand>(_scopeFactory, (p, c, ct) =>
        {
            actualServiceProvider = p;
            actualCommandRecord = c;
            actualCancellationToken = ct;
            return Task.CompletedTask;
        });

        await sut.Handle(command, cts.Token);

        Assert.That(actualServiceProvider, Is.SameAs(_scopeFactory.Provider.Object));
        Assert.That(actualCommandRecord, Is.SameAs(command));
        Assert.That(actualCancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void ShouldNotCatchException()
    {
        var sut = new FuncCommandHandlerWrapper<TestCommand>(_scopeFactory, (_, _, _) =>
            throw new Exception("Test exception"));

        var actualException =
            Assert.ThrowsAsync<Exception>(() => sut.Handle(new TestCommand(), CancellationToken.None));

        Assert.That(actualException, Is.Not.Null);
        Assert.That(actualException!.Message, Is.SameAs("Test exception"));
    }

    [Test]
    public void ShouldNotCatchOperationCancelledException()
    {
        var sut = new FuncCommandHandlerWrapper<TestCommand>(_scopeFactory, (_, _, _) =>
            throw new OperationCanceledException("Test operation cancelled exception"));

        var actualException =
            Assert.ThrowsAsync<OperationCanceledException>(() => sut.Handle(new TestCommand(), CancellationToken.None));

        Assert.That(actualException, Is.Not.Null);
        Assert.That(actualException!.Message, Is.SameAs("Test operation cancelled exception"));
    }

    [Test]
    public async Task ShouldDisposeCreatedScope()
    {
        var sut = new FuncCommandHandlerWrapper<TestCommand>(_scopeFactory, (_, _, _) => Task.CompletedTask);

        await sut.Handle(new TestCommand(), CancellationToken.None);

        _scopeFactory.Scope.Verify(s => s.Dispose(), Times.Once);
    }

    [Test]
    public void ShouldDisposeCreatedScope_IfExceptionThrown()
    {
        var sut = new FuncCommandHandlerWrapper<TestCommand>(_scopeFactory, (_, _, _) =>
            throw new Exception("Test exception"));

        Assert.ThrowsAsync<Exception>(() => sut.Handle(new TestCommand(), CancellationToken.None));

        _scopeFactory.Scope.Verify(s => s.Dispose(), Times.Once);
    }
}