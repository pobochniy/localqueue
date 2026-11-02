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
        // ICommandLease of the handler extends the lock of the command of this async flow
        var lease = new CommandLeaseState(commandRecord, storage);
        CommandLease.Current = lease;
        try
        {
            var command = _serializer.Deserialize<TCommand>(commandRecord.Data)!;
            var sw = Stopwatch.StartNew();

            await _retryRunner.Execute(async () =>
                {
                    // The lock extended by the handler counts, not the one taken at prefetch
                    if (!lease.IsExpired)
                    {
                        try
                        {
                            await _commandHandler(scope.ServiceProvider, command, ct);
                        }
                        catch (Exception e) when (IsLeaseExpired(e, commandRecord.Id))
                        {
                            // The command may already be taken by another instance: no retries, no delete
                            _logger.LogWarning(e, "Command {Id} processing stopped, the lock is over", commandRecord.Id);
                            return;
                        }

                        if (!await storage.Delete(commandRecord.Id, commandRecord.TryCount, ct))
                        {
                            _logger.LogWarning(
                                "Command {Id} is processed, but not deleted: the lock was over and another instance has prefetched it",
                                commandRecord.Id);
                        }

                        var processingTime = sw.Elapsed;
                        var processingLag = DateTime.UtcNow - commandRecord.CreatedAtUtc;
                        LocalQueueMetrics.RecordPostProcessingMetrics(commandRecord, processingTime, processingLag);
                    }
                },
                e => _logger.LogError(e, "Command {Id} processing failed", commandRecord.Id),
                () => RedeliverLater(commandRecord, storage, lease, ct),
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
        finally
        {
            // Work the handler has left running keeps the lease through its execution context
            lease.Release();
        }
    }

    private async Task RedeliverLater(CommandRecord commandRecord, ILocalCommandsQueueWorkerStorage storage,
        CommandLeaseState lease, CancellationToken ct)
    {
        if (lease.IsReleased || _retryPolicy.RedeliverAfter(commandRecord.TryCount) is not { } delay)
        {
            return;
        }

        if (!await storage.MakeInvisibleFor(commandRecord.Id, commandRecord.TryCount, delay, ct))
        {
            _logger.LogWarning(
                "Command {Id} is not hidden after the failure: the lock was over and another instance has prefetched it",
                commandRecord.Id);
        }
    }

    // The handler may let the exception go wrapped: by Task.WhenAll, a library or its own exception
    private static bool IsLeaseExpired(Exception? e, Guid commandId)
    {
        return e switch
        {
            null => false,
            CommandLeaseExpiredException expired => expired.CommandId == commandId,
            AggregateException aggregate => aggregate.InnerExceptions.Any(inner => IsLeaseExpired(inner, commandId)),
            _ => IsLeaseExpired(e.InnerException, commandId)
        };
    }
}
