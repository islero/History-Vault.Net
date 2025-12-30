using HistoryVault.Models;

namespace HistoryVault.Abstractions;

/// <summary>
/// Defines the contract for checking data availability in the vault.
/// </summary>
public interface IDataAvailabilityChecker
{
    /// <summary>
    /// Checks data availability for a specific symbol and timeframe within a date range.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="timeframe">The timeframe to check.</param>
    /// <param name="start">The start of the date range.</param>
    /// <param name="end">The end of the date range.</param>
    /// <param name="scope">The storage scope to check.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A report detailing available and missing data ranges.</returns>
    Task<DataAvailabilityReport> CheckAvailabilityAsync(
        string symbol,
        CandlestickInterval timeframe,
        DateTime start,
        DateTime end,
        StorageScope scope,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the earliest and latest available timestamps for a symbol and timeframe.
    /// </summary>
    /// <param name="symbol">The symbol to query.</param>
    /// <param name="timeframe">The timeframe to query.</param>
    /// <param name="scope">The storage scope to check.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A tuple of (earliest, latest) timestamps, or null if no data exists.</returns>
    Task<(DateTime Earliest, DateTime Latest)?> GetDataBoundsAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if any data exists for a specific symbol.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="scope">The storage scope to check.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>True if data exists; otherwise, false.</returns>
    Task<bool> HasDataAsync(string symbol, StorageScope scope, CancellationToken ct = default);

    /// <summary>
    /// Checks if data exists for a specific symbol and timeframe.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="timeframe">The timeframe to check.</param>
    /// <param name="scope">The storage scope to check.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>True if data exists; otherwise, false.</returns>
    Task<bool> HasDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default);
}
