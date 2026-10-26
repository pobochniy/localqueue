using LocalQueue.MySql;
using LocalQueue.OpenTelemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

namespace LocalQueue.Tests.Hosts;

public sealed class MinimalMySqlStorageHost : IDisposable
{
    private readonly IHost _host;

    public MinimalMySqlStorageHost(string localCommandsQueueTableName)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices((_, services) =>
                    {
                        services
                            .AddLocalCommandQueue(c => c.UseMySqlStorage(
                                new MySqlCommandQueueStorageOptions
                                {
                                    TableName = localCommandsQueueTableName
                                }))
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

    public T GetRequiresService<T>() where T : notnull
    {
        return _host.Services.GetRequiredService<T>();
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}