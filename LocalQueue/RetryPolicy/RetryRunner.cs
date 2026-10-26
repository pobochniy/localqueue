using LocalQueue.Processing;

namespace LocalQueue.RetryPolicy;

internal class RetryRunner
{
    private readonly ICommandRetryPolicy _retryPolicy;

    public RetryRunner(ICommandRetryPolicy retryPolicy)
    {
        _retryPolicy = retryPolicy;
    }

    public async Task Execute(Func<Task> action, Action<Exception> onPerTryFailure, Func<Task> onFailure, CancellationToken ct)
    {
        var maxTries = _retryPolicy.MaxRetryCount;

        for (var tryNumber = 1; tryNumber <= maxTries; tryNumber++)
        {
            if (tryNumber > 1)
            {
                await Task.Delay(_retryPolicy.BackoffFor(tryNumber - 1), ct);
            }

            try
            {
                await action();
                return;
            }
            catch (OperationCanceledException e) when (e.CancellationToken == ct)
            {
                // it's ok
                throw;
            }
            catch (Exception e)
            {
                onPerTryFailure(e);
            }
        }

        await onFailure();
    }
}
