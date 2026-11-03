using LocalQueue.OpenTelemetry;
using LocalQueue.RetryPolicy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

namespace LocalQueue.Tests.Hosts;

public delegate void UseWorkerStorage(LocalCommandQueueWorkerConfigurator configurator, string connectionString,
    string tableName);

/// <summary>
/// Test server with the workers registered by <c>configureWorkers</c> and the metrics endpoint.
/// <c>useStorage</c> sets the worker storage for a connection string and a table.
/// </summary>
public sealed class WorkerHost : IDisposable
{
    private readonly IHost _host;

    private WorkerHost(Action<IServiceCollection> configureWorkers)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices((_, services) =>
                    {
                        configureWorkers(services);
                        services
                            .AddRouting()
                            .AddOpenTelemetry()
                            .WithMetrics(c =>
                            {
                                c.AddLocalCommandQueueInstrumentation();
                                c.AddPrometheusExporter();
                            });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseOpenTelemetryPrometheusScrapingEndpoint();
                    });
            })
            .Build();
    }

    public static WorkerHost Minimal(UseWorkerStorage useStorage, string connectionString, string localCommandsQueueTableName)
    {
        return new WorkerHost(services => services
            .AddSingleton<LocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                useStorage(c, connectionString, localCommandsQueueTableName);
                c.PrefetchCount = 1;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions { MaxRetryCount = 3 });
            }));
    }

    public static WorkerHost Named(UseWorkerStorage useStorage, string connectionString, string localCommandsQueueTableName)
    {
        return new WorkerHost(services => services
            .AddSingleton<NamedLocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                useStorage(c, connectionString, localCommandsQueueTableName);
                c.AddCommandHandler<NamedLocalCommand, NamedLocalCommandHandler>();
            }));
    }

    public static WorkerHost TwoDatabases(UseWorkerStorage useStorage, string connectionString1, string localCommandsQueueTableName1,
        string connectionString2, string localCommandsQueueTableName2)
    {
        return new WorkerHost(services => services
            .AddSingleton<LocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                useStorage(c, connectionString1, localCommandsQueueTableName1);
                c.PrefetchCount = 1;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions { MaxRetryCount = 3 });
            })
            .AddLocalCommandQueueWorker(c =>
            {
                useStorage(c, connectionString2, localCommandsQueueTableName2);
                c.PrefetchCount = 1;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions { MaxRetryCount = 3 });
            }));
    }

    /// <summary>
    /// Two workers on one table, each with its own command type and processing options.
    /// </summary>
    public static WorkerHost TwoWorkersOneTable(UseWorkerStorage useStorage, string connectionString, string localCommandsQueueTableName,
        TimeSpan invisibilityTimeout)
    {
        return new WorkerHost(services => services
            .AddSingleton<LocalCommandHandler>()
            .AddSingleton<OtherLocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                useStorage(c, connectionString, localCommandsQueueTableName);
                c.PrefetchCount = 1;
                c.InvisibilityTimeout = invisibilityTimeout;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            })
            .AddLocalCommandQueueWorker(c =>
            {
                useStorage(c, connectionString, localCommandsQueueTableName);
                c.PrefetchCount = 1;
                c.InvisibilityTimeout = invisibilityTimeout;
                c.AddCommandHandler<OtherLocalCommand, OtherLocalCommandHandler>();
            }));
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

    public HttpClient GetTestClient() => _host.GetTestClient();

    public void Dispose()
    {
        _host.Dispose();
    }
}
