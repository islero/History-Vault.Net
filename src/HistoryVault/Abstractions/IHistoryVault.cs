using HistoryVault.Configuration;
using HistoryVault.Models;

namespace HistoryVault.Abstractions;

/// <summary>
/// Defines the core contract for market data storage and retrieval operations.
/// </summary>
public interface IHistoryVault
{
    /// <summary>
    /// Saves market data to the vault with optional compression and aggregation.
    /// </summary>
    /// <param name="data">The symbol data containing candlesticks to persist.</param>
    /// <param name="options">Save configuration including compression and overwrite behavior.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A task representing the async save operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when data or options is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when target timeframe is smaller than source.</exception>
    Task SaveAsync(SymbolDataV2 data, SaveOptions options, CancellationToken ct = default);

    /// <summary>
    /// Loads market data from the vault for a single symbol or pattern.
    /// </summary>
    /// <param name="options">Load configuration including symbol pattern, date range, and timeframes.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>The loaded symbol data, or null if no matching data is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    Task<SymbolDataV2?> LoadAsync(LoadOptions options, CancellationToken ct = default);

    /// <summary>
    /// Loads market data from the vault for multiple symbols matching a pattern.
    /// </summary>
    /// <param name="options">Load configuration including symbol pattern, date range, and timeframes.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A read-only list of loaded symbol data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    Task<IReadOnlyList<SymbolDataV2>> LoadMultipleAsync(LoadOptions options, CancellationToken ct = default);

    /// <summary>
    /// Checks data availability for a specific symbol and timeframe within a date range.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="timeframe">The timeframe to check.</param>
    /// <param name="start">The start of the date range.</param>
    /// <param name="end">The end of the date range.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A report detailing available and missing data ranges.</returns>
    Task<DataAvailabilityReport> CheckAvailabilityAsync(
        string symbol,
        CandlestickInterval timeframe,
        DateTime start,
        DateTime end,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all symbol names matching a pattern from the vault.
    /// </summary>
    /// <param name="pattern">The glob pattern to match (e.g., "BTC*", "CON.EP.*").</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A read-only list of matching symbol names.</returns>
    Task<IReadOnlyList<string>> GetMatchingSymbolsAsync(string pattern, CancellationToken ct = default);

    /// <summary>
    /// Gets all available timeframes for a specific symbol.
    /// </summary>
    /// <param name="symbol">The symbol to query.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>A read-only list of available timeframes.</returns>
    Task<IReadOnlyList<CandlestickInterval>> GetAvailableTimeframesAsync(
        string symbol,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all data for a specific symbol.
    /// </summary>
    /// <param name="symbol">The symbol to delete.</param>
    /// <param name="scope">The storage scope to delete from.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>True if data was deleted; false if no data existed.</returns>
    Task<bool> DeleteSymbolAsync(string symbol, StorageScope scope, CancellationToken ct = default);

    /// <summary>
    /// Deletes data for a specific symbol and timeframe.
    /// </summary>
    /// <param name="symbol">The symbol to delete.</param>
    /// <param name="timeframe">The timeframe to delete.</param>
    /// <param name="scope">The storage scope to delete from.</param>
    /// <param name="ct">Cancellation token for async operation.</param>
    /// <returns>True if data was deleted; false if no data existed.</returns>
    Task<bool> DeleteTimeframeAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default);
}
