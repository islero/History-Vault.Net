namespace HistoryVault.Models;

/// <summary>
/// Represents market data for a single symbol across multiple timeframes.
/// </summary>
public class SymbolDataV2
{
    /// <summary>
    /// The symbol identifier (e.g., "BTCUSDT", "CON.EP.G25").
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// The collection of timeframe data for this symbol.
    /// </summary>
    public List<TimeframeV2> Timeframes { get; set; } = [];
}
