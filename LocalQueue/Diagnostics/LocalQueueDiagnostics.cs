using System.Diagnostics;
using System.Reflection;

namespace LocalQueue.Diagnostics;

internal static class LocalQueueDiagnostics
{
    private static ActivitySource ActivitySource { get; } = CreateActivitySource();

    public static Activity StartActivity(string name) =>
        ActivitySource.StartActivity(name, ActivityKind.Internal, default(ActivityContext))!;

    private static ActivitySource CreateActivitySource()
    {
        var assembly = typeof(LocalQueueDiagnostics).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        return new ActivitySource("LocalCommandsQueue", version);
    }
}