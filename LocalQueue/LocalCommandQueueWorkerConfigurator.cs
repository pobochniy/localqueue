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
    // Keyed by the command type name (CommandTypes.NameOf): the retry policy and the metrics are keyed by it too
    private readonly Dictionary<string, HandlerFactory> _handlerRegistrations = new();
    // Every name the worker takes the commands by → the command type
    private readonly Dictionary<string, Type> _acceptedCommandTypes = new(StringComparer.Ordinal);
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
        _handlerRegistrations[AddCommandType<TCommand>()] = (serviceScopeFactory, options, storage, deserializer, loggerFactory)
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
        _handlerRegistrations[AddCommandType<TCommand>()] = (serviceScopeFactory, options, storage, deserializer, loggerFactory) => new RawCommandHandler<TCommand>(
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
        if (retryPolicy.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retryPolicy), retryPolicy.MaxAttempts,
                $"{nameof(ICommandRetryPolicy.MaxAttempts)} of {typeof(TCommand).FullName} must be at least 1 or null.");
        }

        RetryOptions[CommandTypes.NameOf<TCommand>()] = retryPolicy;
    }

    // A name taken by two command types would hand the commands of one to the handler of the other
    private string AddCommandType<TCommand>()
    {
        var names = CommandTypes.Of(typeof(TCommand));
        foreach (var accepted in names.Accepted)
        {
            if (_acceptedCommandTypes.TryGetValue(accepted, out var other) && other != typeof(TCommand))
            {
                throw new InvalidOperationException(
                    $"Command type name '{accepted}' of {typeof(TCommand).FullName} is already taken by {other.FullName}.");
            }
        }

        foreach (var accepted in names.Accepted)
        {
            _acceptedCommandTypes[accepted] = typeof(TCommand);
        }

        return names.Name;
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

        // Stateless: the lease follows the command of the current async flow, so handlers of any lifetime may use it
        serviceCollection.TryAddSingleton<ICommandLease, CommandLease>();

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
            _acceptedCommandTypes.Keys.ToArray(),
            commandChannel,
            sp.GetRequiredService<ILogger<CommandFetcher>>()
        ));
        
        serviceCollection.AddSingleton(new StorageSummarySource(_storageFactory,
            _acceptedCommandTypes.ToDictionary(x => x.Key, x => CommandTypes.NameOf(x.Value), StringComparer.Ordinal)));
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