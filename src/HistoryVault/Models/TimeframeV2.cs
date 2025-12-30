namespace HistoryVault.Models;

/// <summary>
/// Represents candlestick data for a specific timeframe with indexing support.
/// </summary>
public sealed class TimeframeV2
{
    /// <summary>
    /// The interval/timeframe of the candlesticks.
    /// </summary>
    public CandlestickInterval Timeframe { get; init; }

    /// <summary>
    /// The starting index for iteration.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// The current index position.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The ending index for iteration.
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    /// Indicates whether there is no more historical data available before StartIndex.
    /// </summary>
    public bool NoMoreHistory { get; set; }

    /// <summary>
    /// The collection of candlesticks for this timeframe.
    /// </summary>
    public List<CandlestickV2> Candlesticks { get; set; } = [];
}
