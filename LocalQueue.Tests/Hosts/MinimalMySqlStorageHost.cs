using LocalQueue.MySql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalQueue.Tests.Hosts;

public sealed class MinimalMySqlStorageHost : IDisposable
{
    private readonly IHost _host;

    public MinimalMySqlStorageHost(string mysqlConnectionString, string localCommandsQueueTableName)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices((context, services) =>
                    {
                        services
                            .AddMySqlCommandsStorage(new CommandStorageOptions
                            {
                                ConnectionString = mysqlConnectionString,
                                TableName = localCommandsQueueTableName
                            })
                            .AddLocalCommandQueue()
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
