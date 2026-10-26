using LocalQueue.Processing;
using LocalQueue.RetryPolicy;

namespace LocalQueue.Tests.Processing;

public static class CommandProcessingOptionsExtensions
{
    public static CommandProcessingOptions WithBackoffIntervalFor<TCommand>(this CommandProcessingOptions options,
        TimeSpan backoffInterval)
    {
        options.RetryOptions.AddOrUpdate<TCommand>(retryOptions => { retryOptions.BackoffInterval = backoffInterval; });

        return options;
    }

    public static CommandProcessingOptions WithRetryAfterFailure<TCommand>(this CommandProcessingOptions options,
        TimeSpan retryAfterFailure)
    {
        options.RetryOptions.AddOrUpdate<TCommand>(retryOptions =>
        {
            retryOptions.RetryAfterFailure = retryAfterFailure;
        });

        return options;
    }
    
    public static CommandProcessingOptions WithoutRetryAfterFailure<TCommand>(this CommandProcessingOptions options)
    {
        options.RetryOptions.AddOrUpdate<TCommand>(retryOptions =>
        {
            retryOptions.RetryAfterFailure = null;
        });

        return options;
    }

    private static void AddOrUpdate<TCommand>(this Dictionary<string, ICommandRetryPolicy> options,
        Action<RetryPolicyOptions> configure)
    {
        var key = typeof(TCommand).FullName!;

        if (!options.TryGetValue(key, out var existing) || existing is not RetryPolicyOptions retryOptions)
        {
            retryOptions = new RetryPolicyOptions();
            options[key] = retryOptions;
        }

        configure(retryOptions);
    }
}