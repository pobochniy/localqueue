using System.Text.Json;
using LocalQueue.Diagnostics;
using LocalQueue.Processing;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LocalQueue;

internal delegate IRawCommandHandler HandlerFactory(
    IServiceScopeFactory serviceScopeFactory,
    CommandProcessingOptions options,
    Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> storageFactory,
    ICommandDeserializer commandDeserializer,
    ILoggerFactory loggerFactory);

/// <summary>
/// Local command queue worker configurator
/// </summary>
public class LocalCommandQueueWorkerConfigurator : CommandProcessingOptions
{
    private readonly Dictionary<string, HandlerFactory> _handlerRegistrations = new();
    private Func<IServiceProvider, ILocalCommandsQueueWorkerStorage>? _storageFactory;

    /// <summary>
    /// Add storage registration
    /// </summary>
    /// <param name="storage">The instance of storage.</param>
    public LocalCommandQueueWorkerConfigurator UseStorage(ILocalCommandsQueueWorkerStorage storage)
    {
        _storageFactory = _ => storage;
        return this;
    }
    
    /// <summary>
    /// Add storage registration
    /// </summary>
    /// <param name="storageFactory">Factory to create instance of storage.</param>
    public LocalCommandQueueWorkerConfigurator UseStorage(Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> storageFactory)
    {
        _storageFactory = storageFactory;
        return this;
    }

    /// <summary>
    /// Add handler of type <typeparam name="THandler"/> to process command of type <typeparam name="TCommand"/>
    /// </summary>
    /// <param name="retryOptions">Retry options for command processing. </param>
    public LocalCommandQueueWorkerConfigurator
        AddCommandHandler<TCommand, THandler>(ICommandRetryPolicy? retryOptions = null)
        where THandler : class, ICommandHandler<TCommand>
    {
        AddCommandHandler<TCommand, THandler>(h => h.Handle, retryOptions);
        return this;
    }

    /// <summary>
    /// Add handler to process command of type <typeparam name="TCommand"/>
    /// </summary>
    /// <param name="funcHandler">The command processing function to be executed asynchronously. </param>
    /// <param name="retryOptions">Retry options for command processing. </param>
    public LocalCommandQueueWorkerConfigurator AddCommandHandler<TCommand>(FuncCommandHandler<TCommand> funcHandler,
        ICommandRetryPolicy? retryOptions = null)
    {
        _handlerRegistrations[typeof(TCommand).FullName!] = (serviceScopeFactory, options, storage, deserializer, loggerFactory)
            => new RawCommandHandler<TCommand>(
                options,
                storage,
                funcHandler,
                serviceScopeFactory,
                deserializer,
                loggerFactory.CreateLogger<RawCommandHandler<TCommand>>());

        ConfigureRetryPolicy<TCommand>(retryOptions);
        return this;
    }

    /// <summary>
    /// Add handler of type <typeparam name="THandler"/> to process command of type <typeparam name="TCommand"/>
    /// </summary>
    /// <param name="process">The command processing function to be executed asynchronously. </param>
    /// <param name="retryOptions">Retry options for command processing. </param>
    public LocalCommandQueueWorkerConfigurator AddCommandHandler<TCommand, THandler>(
        Func<THandler, Func<TCommand, CancellationToken, Task>> process,
        ICommandRetryPolicy? retryOptions = null) where THandler : notnull
    {
        _handlerRegistrations[typeof(TCommand).FullName!] = (serviceScopeFactory, options, storage, deserializer, loggerFactory) => new RawCommandHandler<TCommand>(
            options,
            storage,
            (provider, command, ct) => process(provider.GetRequiredService<THandler>())(command, ct),
            serviceScopeFactory,
            deserializer,
            loggerFactory.CreateLogger<RawCommandHandler<TCommand>>());

        ConfigureRetryPolicy<TCommand>(retryOptions);
        return this;
    }

    private void ConfigureRetryPolicy<TCommand>(ICommandRetryPolicy? retryPolicy)
    {
        if (retryPolicy == null) return;
        RetryOptions.Add(typeof(TCommand).FullName!, retryPolicy);
    }

    internal void RegisterWorker(IServiceCollection serviceCollection)
    {
        if (_storageFactory == null)
        {
            throw new InvalidOperationException("No storage configured.");
        }

        if (_handlerRegistrations.Count == 0)
        {
            throw new InvalidOperationException("No command handlers configured.");
        }

        var commandChannel = new FetchCommandChannel(PrefetchCount);
        serviceCollection.AddSingleton(sp =>
        {
            var serializer = new JsonCommandSerializer(_serializerOptions);
            
            var handlers = _handlerRegistrations
                .Select(handlerFactory => handlerFactory.Value(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    this, 
                    _storageFactory,
                    serializer,
                    sp.GetRequiredService<ILoggerFactory>()));
            return new CommandProcessor(
                this,
                commandChannel,
                handlers,
                sp.GetRequiredService<ILogger<CommandProcessor>>());
        });

        serviceCollection.AddSingleton(sp => new CommandFetcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _storageFactory,
            this,
            _handlerRegistrations.Keys.ToArray(),
            commandChannel,
            sp.GetRequiredService<ILogger<CommandFetcher>>()
        ));
        
        serviceCollection.AddSingleton(new StorageSummarySource(_storageFactory, _handlerRegistrations.Keys.ToArray()));
        serviceCollection.TryAddSingleton(sp => new StorageSummaryCollector(
            sp.GetServices<StorageSummarySource>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<StorageSummaryCollector>>()
        ));

        serviceCollection.AddHostedService<FetchCommandHostedService>();
        serviceCollection.AddHostedService<CommandProcessorHostedService>();
        serviceCollection.AddHostedService<StorageSummaryHostedService>();
    }

    private readonly JsonSerializerOptions _serializerOptions = new();

    /// <summary>
    /// Options to control the behavior during serialization/deserialization.
    /// </summary>
    /// <param name="configure"></param>
    public void ConfigureSerializer(Action<JsonSerializerOptions> configure)
    {
        CommandSerializersConfiguration.ConfigureSerializer(_serializerOptions, configure);
    }
}