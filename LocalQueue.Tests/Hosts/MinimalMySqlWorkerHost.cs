using LocalQueue.MySql;
using LocalQueue.RetryPolicy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocalQueue.Tests.Hosts;

public sealed class MinimalMySqlWorkerHost : IDisposable
{
    private readonly IHost _host;

    public MinimalMySqlWorkerHost(string mysqlConnectionString, string localCommandsQueueTableName)
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
                            .AddSingleton<LocalCommandHandler>()
                            .AddLocalCommandQueueWorker(c =>
                            {
                                c.PrefetchCount = 1;
                                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions {MaxRetryCount = 3});
                            })
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
