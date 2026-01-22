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
    /// Gets the percentage of the queried range that has data coverage (as a ratio from 0.0 to 1.0).
    /// Use the :P format specifier when displaying (e.g., $"{CoveragePercentage:P2}").
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

    /// <summary>
    /// Calculates the percentage of coverage for available data within the specified query range.
    /// </summary>
    /// <returns>
    /// A double value representing the proportion of data available relative to the total query duration.
    /// The result is a number between 0.0 and 1.0, where 1.0 indicates full coverage and 0.0 indicates no coverage.
    /// </returns>
    private double CalculateCoverage()
    {
        if (QueryStart >= QueryEnd)
        {
            return 0.0;
        }

        double totalQueryDuration = (QueryEnd - QueryStart).TotalSeconds;
        if (totalQueryDuration <= 0)
        {
            return 0.0;
        }

        double availableDuration = AvailableRanges.Sum(r => r.Duration.TotalSeconds);
        double coverage = availableDuration / totalQueryDuration;

        return Math.Min(1.0, Math.Max(0.0, coverage));
    }
}
