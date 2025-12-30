using HistoryVault.Models;

namespace HistoryVault.Configuration;

/// <summary>
/// Configuration options for loading market data from the vault.
/// </summary>
public sealed class LoadOptions
{
    /// <summary>
    /// Gets or sets the symbol or wildcard pattern to load.
    /// Supports glob patterns: "CON.EP.*", "BTC*", "*.EP.?25"
    /// </summary>
    public required string Symbol { get; set; }

    /// <summary>
    /// Gets or sets the timeframes to load.
    /// If null or empty, all available timeframes are loaded.
    /// </summary>
    public CandlestickInterval[]? Timeframes { get; set; }

    /// <summary>
    /// Gets or sets the start date of the range to load (inclusive).
    /// If null, loads from the earliest available data.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Gets or sets the end date of the range to load (inclusive).
    /// If null, loads until the latest available data.
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Gets or sets the number of extra candlesticks to load before StartDate for each timeframe.
    /// This is useful for warming up indicators that require historical lookback.
    /// Default is 0.
    /// </summary>
    public int WarmupCandlesCount { get; set; }

    /// <summary>
    /// Gets or sets the storage scope to load from.
    /// Default is <see cref="StorageScope.Local"/>.
    /// </summary>
    public StorageScope Scope { get; set; } = StorageScope.Local;

    /// <summary>
    /// Gets or sets whether to create missing timeframes on-the-fly by aggregating from available data.
    /// When true, if a requested timeframe doesn't exist but a smaller one does, it will be aggregated.
    /// Default is false.
    /// </summary>
    public bool AllowAggregation { get; set; }

    /// <summary>
    /// Gets or sets whether to include partial candlesticks at range boundaries.
    /// Default is true.
    /// </summary>
    public bool IncludePartialCandles { get; set; } = true;

    /// <summary>
    /// Creates load options for a single symbol with a specific date range.
    /// </summary>
    /// <param name="symbol">The symbol to load.</param>
    /// <param name="startDate">The start of the date range.</param>
    /// <param name="endDate">The end of the date range.</param>
    /// <param name="timeframes">The timeframes to load.</param>
    /// <returns>A new <see cref="LoadOptions"/> instance.</returns>
    public static LoadOptions ForSymbol(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        params CandlestickInterval[] timeframes)
    {
        return new LoadOptions
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate,
            Timeframes = timeframes.Length > 0 ? timeframes : null
        };
    }

    /// <summary>
    /// Creates load options with wildcard pattern matching.
    /// </summary>
    /// <param name="pattern">The glob pattern to match symbols.</param>
    /// <param name="timeframes">The timeframes to load.</param>
    /// <returns>A new <see cref="LoadOptions"/> instance.</returns>
    public static LoadOptions ForPattern(string pattern, params CandlestickInterval[] timeframes)
    {
        return new LoadOptions
        {
            Symbol = pattern,
            Timeframes = timeframes.Length > 0 ? timeframes : null
        };
    }
}
