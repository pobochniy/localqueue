using System.Text.Json;
using LocalQueue.Processing;
using LocalQueue.RetryPolicy;
using LocalQueue.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LocalQueue;

/// <summary>
/// Local command queue worker configurator
/// </summary>
public class LocalCommandQueueWorkerConfigurator : CommandProcessingOptions
{
    private readonly List<Action<IServiceCollection>> _handlerRegistrations = new();

    /// <summary>
    /// Add handler of type <typeparam name="THandler"/> to process command of type <typeparam name="TCommand"/>
    /// </summary>
    /// <param name="retryOptions">Retry options for command processing. </param>
    public void AddCommandHandler<TCommand, THandler>(RetryPolicyOptions? retryOptions = null) where THandler : class, ICommandHandler<TCommand>
    {
        AddCommandHandler<TCommand, THandler>(h => h.Handle, retryOptions);
    }

    /// <summary>
    /// Add handler to process command of type <typeparam name="TCommand"/>
    /// </summary>
    /// <param name="funcHandler">The command processing function to be executed asynchronously. </param>
    /// <param name="retryOptions">Retry options for command processing. </param>
    public void AddCommandHandler<TCommand>(FuncCommandHandler<TCommand> funcHandler, RetryPolicyOptions? retryOptions = null)
    {
        _handlerRegistrations.Add(services =>
        {
            services.AddSingleton<ICommandHandler<TCommand>>(
                sp => new FuncCommandHandlerWrapper<TCommand>(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    funcHandler));
            services.AddSingleton<IRawCommandHandler, RawCommandHandler<TCommand>>();
        });

        ConfigureRetryPolicy<TCommand>(retryOptions);
    }

    /// <summary>
    /// Add handler of type <typeparam name="THandler"/> to process command of type <typeparam name="TCommand"/>
    /// </summary>
    /// <param name="process">The command processing function to be executed asynchronously. </param>
    /// <param name="retryOptions">Retry options for command processing. </param>
    public void AddCommandHandler<TCommand, THandler>(
        Func<THandler, Func<TCommand, CancellationToken, Task>> process,
        RetryPolicyOptions? retryOptions = null) where THandler : notnull
    {
        _handlerRegistrations.Add(services =>
        {
            services.AddSingleton<ICommandHandler<TCommand>>(
                sp => new FuncCommandHandlerWrapper<TCommand>(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    (provider, command, ct) => process(provider.GetRequiredService<THandler>())(command, ct)));
            services.AddSingleton<IRawCommandHandler, RawCommandHandler<TCommand>>();
        });

        ConfigureRetryPolicy<TCommand>(retryOptions);
    }

    private void ConfigureRetryPolicy<TCommand>(RetryPolicyOptions? retryPolicyOptions)
    {
        if (retryPolicyOptions == null) return;
        RetryOptions.Add(typeof(TCommand).FullName!, retryPolicyOptions);
    }

    internal void RegisterHandlers(IServiceCollection serviceCollection)
    {
        foreach (var handlerRegistration in _handlerRegistrations)
        {
            handlerRegistration(serviceCollection);
        }
        
        serviceCollection.AddSingleton<CommandProcessingOptions>(this);
        serviceCollection.AddHostedService<StorageSummaryHostedService>();
        
        serviceCollection.TryAddSingleton(new FetchCommandChannel(PrefetchCount));

        serviceCollection.TryAddSingleton<CommandProcessor>();
        serviceCollection.AddHostedService<FetchCommandHostedService>();
        serviceCollection.AddHostedService<CommandProcessorHostedService>();
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

    internal void RegisterSerializers(IServiceCollection serviceCollection)
    {
        CommandSerializersConfiguration.RegisterSerializers(_serializerOptions, serviceCollection);
    }
}
