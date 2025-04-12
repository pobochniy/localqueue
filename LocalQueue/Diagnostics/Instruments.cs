namespace LocalQueue.Diagnostics;

/// <summary>
/// Dodo.LocalCommandQueue metrics
/// </summary>
public static class Instruments
{
    /// <summary>
    /// Processing time histogram name
    /// </summary>
    public const string CommandProcessingTimeHistogramName = "local-command-queue-processing-time";

    /// <summary>
    /// Processing lag histogram name
    /// </summary>
    public const string CommandProcessingLagHistogramName = "local-command-queue-processing-lag";

    /// <summary>
    /// Processing lag and processing time recommended histogram boundaries
    /// </summary>
    public static readonly double[] HistogramBoundaries = [0.1, 0.5, 1, 2, 5, 10, 30, 60, 2 * 60, 5 * 60, 10 * 60];

    /// <summary>
    /// Dodo.LocalCommandQueue <see cref="System.Diagnostics.Metrics.Meter"/> name
    /// </summary>
    public static readonly string MeterName = typeof(Instruments).Assembly.GetName().Name!;
}