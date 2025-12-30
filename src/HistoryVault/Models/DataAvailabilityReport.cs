namespace HistoryVault.Models;

/// <summary>
/// Represents a report on data availability for a specific symbol and timeframe within a date range.
/// </summary>
public sealed class DataAvailabilityReport
{
    /// <summary>
    /// The symbol that was queried.
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// The timeframe that was queried.
    /// </summary>
    public required CandlestickInterval Timeframe { get; init; }

    /// <summary>
    /// The start of the queried date range.
    /// </summary>
    public required DateTime QueryStart { get; init; }

    /// <summary>
    /// The end of the queried date range.
    /// </summary>
    public required DateTime QueryEnd { get; init; }

    /// <summary>
    /// The date ranges where data is available.
    /// </summary>
    public required IReadOnlyList<DateRange> AvailableRanges { get; init; }

    /// <summary>
    /// The date ranges where data is missing.
    /// </summary>
    public required IReadOnlyList<DateRange> MissingRanges { get; init; }

    /// <summary>
    /// The total number of candlesticks available in the queried range.
    /// </summary>
    public int TotalCandlesAvailable { get; init; }

    /// <summary>
    /// The expected number of candlesticks for full coverage.
    /// </summary>
    public int ExpectedCandlesCount { get; init; }

    /// <summary>
    /// Gets the percentage of the queried range that has data coverage.
    /// </summary>
    public double CoveragePercentage => CalculateCoverage();

    /// <summary>
    /// Indicates whether there is full data coverage for the queried range.
    /// </summary>
    public bool HasFullCoverage => MissingRanges.Count == 0;

    /// <summary>
    /// Indicates whether any data is available for the queried range.
    /// </summary>
    public bool HasAnyData => AvailableRanges.Count > 0;

    private double CalculateCoverage()
    {
        if (QueryStart >= QueryEnd)
        {
            return 0.0;
        }

        var totalQueryDuration = (QueryEnd - QueryStart).TotalSeconds;
        if (totalQueryDuration <= 0)
        {
            return 0.0;
        }

        var availableDuration = AvailableRanges.Sum(r => r.Duration.TotalSeconds);
        var coverage = availableDuration / totalQueryDuration * 100.0;

        return Math.Min(100.0, Math.Max(0.0, coverage));
    }
}
