namespace HistoryVault.Models;

/// <summary>
/// Represents a single OHLCV candlestick with open/close times.
/// This is the core data structure for market data storage.
/// </summary>
public sealed class CandlestickV2
{
    /// <summary>
    /// The opening time of the candlestick period.
    /// </summary>
    public DateTime OpenTime { get; set; }

    /// <summary>
    /// The opening price of the candlestick.
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// The highest price during the candlestick period.
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// The lowest price during the candlestick period.
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// The closing price of the candlestick.
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// The closing time of the candlestick period.
    /// </summary>
    public DateTime CloseTime { get; set; }

    /// <summary>
    /// The total volume traded during the candlestick period.
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// Creates a deep copy of this candlestick.
    /// </summary>
    /// <returns>A new <see cref="CandlestickV2"/> instance with identical values.</returns>
    public CandlestickV2 Clone()
    {
        return new CandlestickV2
        {
            OpenTime = OpenTime,
            Open = Open,
            High = High,
            Low = Low,
            Close = Close,
            CloseTime = CloseTime,
            Volume = Volume
        };
    }
}
