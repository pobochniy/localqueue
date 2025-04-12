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
    private readonly RetryPolicy.RetryPolicy _retryPolicy;
    private readonly ICommandDeserializer _serializer;

    public string CommandType { get; } = typeof(TCommand).FullName!;

    public RawCommandHandler(
        CommandProcessingOptions options,
        IServiceScopeFactory scopeFactory,
        ICommandDeserializer serializer,
        ILogger<RawCommandHandler<TCommand>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _serializer = serializer;
        _retryPolicy = new RetryPolicy.RetryPolicy(options.RetryOptions.TryGetValue(CommandType, out var retryOptions)
            ? retryOptions 
            : new RetryPolicyOptions());
    }

    public bool CanHandle(CommandRecord commandRecord)
    {
        return commandRecord.CommandType == CommandType;
    }

    public async Task Handle(CommandRecord commandRecord, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>();
            var storage = scope.ServiceProvider.GetRequiredService<ICommandsStorage>();
            var command = _serializer.Deserialize<TCommand>(commandRecord.Data)!;
            var sw = Stopwatch.StartNew();

            await _retryPolicy.Execute(async () =>
            {
                if (!commandRecord.IsLockExpired)
                {
                    await handler.Handle(command, ct);
                    await storage.Delete(commandRecord.Id, ct);

                    var processingTime = sw.Elapsed;
                    var processingLag = DateTime.UtcNow - commandRecord.CreatedAtUtc;
                    LocalQueueMetrics.RecordPostProcessingMetrics(commandRecord, processingTime, processingLag);
                }
            }, 
            e => _logger.LogError(e, "Command {Id} processing failed", commandRecord.Id),
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