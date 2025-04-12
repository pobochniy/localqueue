using LocalQueue.Processing;
using LocalQueue.RetryPolicy;

namespace LocalQueue.Tests.Processing;

public static class CommandProcessingOptionsExtensions
{
    public static CommandProcessingOptions WithBackoffIntervalFor<TCommand>(this CommandProcessingOptions options, TimeSpan backoffInterval)
    {
        options.RetryOptions.Add(
            typeof(TCommand).FullName!,
            new RetryPolicyOptions {BackoffInterval = backoffInterval});
        return options;
    }   
}