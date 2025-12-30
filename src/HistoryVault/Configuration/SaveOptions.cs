using System.IO.Compression;
using HistoryVault.Models;

namespace HistoryVault.Configuration;

/// <summary>
/// Configuration options for saving market data to the vault.
/// </summary>
public sealed class SaveOptions
{
    /// <summary>
    /// Gets or sets whether to use GZip compression when saving data.
    /// Compression reduces disk usage but increases load time.
    /// Default is true.
    /// </summary>
    public bool UseCompression { get; set; } = true;

    /// <summary>
    /// Gets or sets the compression level when <see cref="UseCompression"/> is true.
    /// Default is <see cref="CompressionLevel.Optimal"/>.
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// Gets or sets whether to merge with existing data, only overwriting overlapping date ranges.
    /// When false, existing data for the symbol/timeframe is completely replaced.
    /// Default is true.
    /// </summary>
    public bool AllowPartialOverwrite { get; set; } = true;

    /// <summary>
    /// Gets or sets the storage scope (Local or Global).
    /// Default is <see cref="StorageScope.Local"/>.
    /// </summary>
    public StorageScope Scope { get; set; } = StorageScope.Local;

    /// <summary>
    /// Gets or sets the target timeframes to persist.
    /// If specified, the data will be aggregated to these timeframes before saving.
    /// If null or empty, data is saved in its original timeframe(s).
    /// </summary>
    public CandlestickInterval[]? TargetTimeframes { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically aggregate larger timeframes from the smallest input timeframe.
    /// When true, if TargetTimeframes contains larger intervals than the source, they will be derived by aggregation.
    /// Default is false.
    /// </summary>
    public bool AggregateFromSmallest { get; set; }

    /// <summary>
    /// Gets or sets the batch size for writing candlesticks.
    /// Larger batches reduce I/O overhead but increase memory usage.
    /// Default is 10000.
    /// </summary>
    public int BatchSize { get; set; } = 10000;

    /// <summary>
    /// Creates a default save options instance.
    /// </summary>
    /// <returns>A new <see cref="SaveOptions"/> with default values.</returns>
    public static SaveOptions Default => new();

    /// <summary>
    /// Creates save options optimized for speed (no compression).
    /// </summary>
    /// <returns>A new <see cref="SaveOptions"/> configured for fast saves.</returns>
    public static SaveOptions Fast => new()
    {
        UseCompression = false,
        AllowPartialOverwrite = false
    };

    /// <summary>
    /// Creates save options optimized for minimal disk usage.
    /// </summary>
    /// <returns>A new <see cref="SaveOptions"/> configured for small file sizes.</returns>
    public static SaveOptions Compact => new()
    {
        UseCompression = true,
        CompressionLevel = CompressionLevel.SmallestSize
    };
}
