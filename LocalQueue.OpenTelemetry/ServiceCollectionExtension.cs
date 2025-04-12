using LocalQueue.Diagnostics;
using OpenTelemetry.Metrics;

namespace LocalQueue.OpenTelemetry;

/// <summary>
/// Methods to register metrics by OpenTelemetry
/// </summary>
public static class ServiceCollectionExtension
{
    /// <summary>
    /// Register OpenTelemetry instruments for local command queue
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static MeterProviderBuilder AddDodoLocalCommandQueueInstrumentation(this MeterProviderBuilder builder)
    {
        builder
            .AddMeter(Instruments.MeterName)
            .AddView(Instruments.CommandProcessingLagHistogramName,
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = Instruments.HistogramBoundaries
                })
            .AddView(Instruments.CommandProcessingTimeHistogramName,
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = Instruments.HistogramBoundaries
                });

        return builder;
    }
}