using HistoryVault.Models;

namespace HistoryVault.Abstractions;

/// <summary>
/// Defines the contract for aggregating candlesticks from smaller to larger timeframes.
/// </summary>
public interface ICandlestickAggregator
{
    /// <summary>
    /// Aggregates candlesticks from a smaller timeframe to a larger timeframe.
    /// </summary>
    /// <param name="candles">The source candlesticks in the smaller timeframe.</param>
    /// <param name="sourceTimeframe">The source timeframe of the input candlesticks.</param>
    /// <param name="targetTimeframe">The target timeframe to aggregate to.</param>
    /// <returns>A list of aggregated candlesticks in the target timeframe.</returns>
    /// <exception cref="ArgumentException">Thrown when source timeframe is larger than or equal to target.</exception>
    /// <exception cref="InvalidOperationException">Thrown when timeframes are not compatible for aggregation.</exception>
    IReadOnlyList<CandlestickV2> Aggregate(
        IReadOnlyList<CandlestickV2> candles,
        CandlestickInterval sourceTimeframe,
        CandlestickInterval targetTimeframe);

    /// <summary>
    /// Aggregates a group of candlesticks into a single candlestick.
    /// </summary>
    /// <param name="candles">The candlesticks to aggregate.</param>
    /// <returns>A single aggregated candlestick.</returns>
    /// <exception cref="ArgumentException">Thrown when the candle collection is empty.</exception>
    CandlestickV2 AggregateToSingle(IReadOnlyList<CandlestickV2> candles);

    /// <summary>
    /// Determines whether aggregation is possible between two timeframes.
    /// </summary>
    /// <param name="sourceTimeframe">The source timeframe.</param>
    /// <param name="targetTimeframe">The target timeframe.</param>
    /// <returns>True if aggregation is possible; otherwise, false.</returns>
    bool CanAggregate(CandlestickInterval sourceTimeframe, CandlestickInterval targetTimeframe);

    /// <summary>
    /// Gets all possible target timeframes that can be aggregated from a source timeframe.
    /// </summary>
    /// <param name="sourceTimeframe">The source timeframe.</param>
    /// <returns>An enumerable of valid target timeframes.</returns>
    IEnumerable<CandlestickInterval> GetPossibleTargetTimeframes(CandlestickInterval sourceTimeframe);
}
