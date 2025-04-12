namespace LocalQueue.RetryPolicy;

internal class RetryPolicy
{
    private readonly RetryPolicyOptions _retryPolicyOptions;
    
    public RetryPolicy(RetryPolicyOptions? options = null)
    {
        _retryPolicyOptions = options ?? new RetryPolicyOptions();
    }

    public async Task Execute(Func<Task> action, Action<Exception> onError, CancellationToken ct)
    {
        var tryNumber = 0;

        while (++tryNumber <= _retryPolicyOptions.MaxRetryCount)
        {
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
                onError(e);
                await Task.Delay(_retryPolicyOptions.BackoffInterval, ct);
            }
        }
    }
}