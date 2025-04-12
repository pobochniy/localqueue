using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LocalQueue.Tests.Fakes;

public class ServiceScopeFactoryFake : IServiceScopeFactory
{
    private readonly Mock<IServiceProvider> _provider;
    private readonly Mock<IServiceScope> _scope;

    public Mock<IServiceScope> Scope => _scope;
    public Mock<IServiceProvider> Provider => _provider;

    public ServiceScopeFactoryFake()
    {
        _provider = new();
        _scope = new Mock<IServiceScope>();
        _scope
            .SetupGet(s => s.ServiceProvider)
            .Returns(_provider.Object);
    }

    public void SetService<TService>(TService instance)
    {
        _provider
            .Setup(p => p.GetService(typeof(TService)))
            .Returns(instance);
    }

    public IServiceScope CreateScope()
    {
        return _scope.Object;
    }
}