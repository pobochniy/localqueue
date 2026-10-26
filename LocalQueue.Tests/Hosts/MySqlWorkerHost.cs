using LocalQueue.MySql;
using LocalQueue.OpenTelemetry;
using LocalQueue.RetryPolicy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

namespace LocalQueue.Tests.Hosts;

/// <summary>
/// Test server with the workers registered by <c>configureWorkers</c> and the metrics endpoint.
/// </summary>
public sealed class MySqlWorkerHost : IDisposable
{
    private readonly IHost _host;

    private MySqlWorkerHost(Action<IServiceCollection> configureWorkers)
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
                                c.AddDodoLocalCommandQueueInstrumentation();
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

    public static MySqlWorkerHost Minimal(string mysqlConnectionString, string localCommandsQueueTableName)
    {
        return new MySqlWorkerHost(services => services
            .AddSingleton<LocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                c.UseMySqlStorage(options =>
                {
                    options.ConnectionString = mysqlConnectionString;
                    options.TableName = localCommandsQueueTableName;
                });
                c.PrefetchCount = 1;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions { MaxRetryCount = 3 });
            }));
    }

    public static MySqlWorkerHost TwoDatabases(string mysqlConnectionString1, string localCommandsQueueTableName1,
        string mysqlConnectionString2, string localCommandsQueueTableName2)
    {
        return new MySqlWorkerHost(services => services
            .AddSingleton<LocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                c.UseMySqlStorage(options =>
                {
                    options.ConnectionString = mysqlConnectionString1;
                    options.TableName = localCommandsQueueTableName1;
                });
                c.PrefetchCount = 1;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions { MaxRetryCount = 3 });
            })
            .AddLocalCommandQueueWorker(c =>
            {
                c.UseMySqlStorage(options =>
                {
                    options.ConnectionString = mysqlConnectionString2;
                    options.TableName = localCommandsQueueTableName2;
                });
                c.PrefetchCount = 1;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>(new RetryPolicyOptions { MaxRetryCount = 3 });
            }));
    }

    /// <summary>
    /// Two workers on one table, each with its own command type and processing options.
    /// </summary>
    public static MySqlWorkerHost TwoWorkersOneTable(string mysqlConnectionString, string localCommandsQueueTableName,
        TimeSpan invisibilityTimeout)
    {
        return new MySqlWorkerHost(services => services
            .AddSingleton<LocalCommandHandler>()
            .AddSingleton<OtherLocalCommandHandler>()
            .AddLocalCommandQueueWorker(c =>
            {
                c.UseMySqlStorage(options =>
                {
                    options.ConnectionString = mysqlConnectionString;
                    options.TableName = localCommandsQueueTableName;
                });
                c.PrefetchCount = 1;
                c.InvisibilityTimeout = invisibilityTimeout;
                c.AddCommandHandler<LocalCommand, LocalCommandHandler>();
            })
            .AddLocalCommandQueueWorker(c =>
            {
                c.UseMySqlStorage(options =>
                {
                    options.ConnectionString = mysqlConnectionString;
                    options.TableName = localCommandsQueueTableName;
                });
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
