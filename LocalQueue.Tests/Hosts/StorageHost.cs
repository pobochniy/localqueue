using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalQueue.Tests.Hosts;

/// <summary>
/// Test server with the commands queue on the storage set by <c>useStorage</c>.
/// </summary>
public sealed class StorageHost : IDisposable
{
    private readonly IHost _host;

    public StorageHost(Action<LocalCommandQueueConfigurator> useStorage)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices((_, services) =>
                    {
                        services
                            .AddLocalCommandQueue(useStorage)
                            .AddRouting();
                    })
                    .Configure(app => { EndpointRoutingApplicationBuilderExtensions.UseRouting(app); });
            })
            .Build();
    }

    public Task StartAsync()
    {
        return _host.StartAsync();
    }

    public Task StopAsync()
    {
        return _host.StopAsync();
    }

    public T GetRequiredService<T>() where T : notnull
    {
        return _host.Services.GetRequiredService<T>();
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
