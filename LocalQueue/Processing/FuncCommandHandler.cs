using Microsoft.Extensions.DependencyInjection;

namespace LocalQueue.Processing;

/// <summary>
/// The command processing function to be executed asynchronously.
/// </summary>
public delegate Task FuncCommandHandler<in TCommand>(IServiceProvider provider, TCommand command, CancellationToken ct);

internal class FuncCommandHandlerWrapper<TCommand> : ICommandHandler<TCommand>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FuncCommandHandler<TCommand> _handler;

    public FuncCommandHandlerWrapper(IServiceScopeFactory scopeFactory, FuncCommandHandler<TCommand> handler)
    {
        _scopeFactory = scopeFactory;
        _handler = handler;
    }

    public async Task Handle(TCommand command, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        await _handler(scope.ServiceProvider, command, ct);
    }
}