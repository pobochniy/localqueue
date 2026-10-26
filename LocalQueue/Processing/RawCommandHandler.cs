using System.Diagnostics;
using LocalQueue.Diagnostics;
using LocalQueue.RetryPolicy;
using LocalQueue.Serialization;
using LocalQueue.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalQueue.Processing;

internal class RawCommandHandler<TCommand> : IRawCommandHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RawCommandHandler<TCommand>> _logger;
    private readonly RetryRunner _retryRunner;
    private readonly ICommandDeserializer _serializer;
    private readonly ICommandRetryPolicy _retryPolicy;
    private readonly Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> _storageFactory;
    private readonly FuncCommandHandler<TCommand> _commandHandler;

    public string CommandType { get; } = typeof(TCommand).FullName!;

    public RawCommandHandler(
        CommandProcessingOptions options,
        Func<IServiceProvider, ILocalCommandsQueueWorkerStorage> storageFactory,
        FuncCommandHandler<TCommand> commandHandler,
        IServiceScopeFactory scopeFactory,
        ICommandDeserializer serializer,
        ILogger<RawCommandHandler<TCommand>> logger)
    {
        _storageFactory = storageFactory;
        _commandHandler = commandHandler;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _serializer = serializer;
        _retryPolicy = options.RetryOptions.TryGetValue(CommandType, out var retryPolicy)
            ? retryPolicy
            : new RetryPolicyOptions();
        _retryRunner = new RetryRunner(_retryPolicy);
    }

    public bool CanHandle(CommandRecord commandRecord)
    {
        return commandRecord.CommandType == CommandType;
    }

    public async Task Handle(CommandRecord commandRecord, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = _storageFactory(scope.ServiceProvider);
        try
        {
            var command = _serializer.Deserialize<TCommand>(commandRecord.Data)!;
            var sw = Stopwatch.StartNew();

            await _retryRunner.Execute(async () =>
                {
                    if (!commandRecord.IsLockExpired)
                    {
                        await _commandHandler(scope.ServiceProvider, command, ct);
                        await storage.Delete(commandRecord.Id, ct);

                        var processingTime = sw.Elapsed;
                        var processingLag = DateTime.UtcNow - commandRecord.CreatedAtUtc;
                        LocalQueueMetrics.RecordPostProcessingMetrics(commandRecord, processingTime, processingLag);
                    }
                },
                e => _logger.LogError(e, "Command {Id} processing failed", commandRecord.Id),
                () => _retryPolicy.RedeliverAfter(commandRecord.TryCount) is { } delay
                    ? storage.MakeInvisibleFor(commandRecord.Id, delay, ct)
                    : Task.CompletedTask,
                ct);
        }
        catch (OperationCanceledException)
        {
            //it's ok
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Command {Id} processing failed", commandRecord.Id);
        }
    }
}