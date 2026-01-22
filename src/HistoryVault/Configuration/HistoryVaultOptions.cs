using HistoryVault.Models;

namespace HistoryVault.Configuration;

/// <summary>
/// Global configuration options for the HistoryVault storage system.
/// </summary>
public sealed class HistoryVaultOptions
{
    /// <summary>
    /// Gets or sets the default storage scope for all operations.
    /// Default is <see cref="StorageScope.Local"/>.
    /// </summary>
    public StorageScope DefaultScope { get; set; } = StorageScope.Local;

    /// <summary>
    /// Gets or sets a custom base path that overrides the automatic path resolution.
    /// When set, this path is used as the base for both Local and Global scopes.
    /// Primarily intended for testing scenarios.
    /// If null (default), paths are automatically resolved based on <see cref="StorageScope"/>:
    /// - Local: ./data/history-vault/ (relative to current working directory)
    /// - Global: OS temp directory/HistoryVault (e.g., /tmp/HistoryVault on Unix)
    /// </summary>
    public string? BasePathOverride { get; set; }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for I/O operations.
    /// Default is Environment.ProcessorCount.
    /// </summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets whether to enable memory-mapped file reads for large datasets.
    /// This can significantly improve read performance for files larger than BufferThresholdBytes.
    /// Default is true.
    /// </summary>
    public bool UseMemoryMappedFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets the file size threshold in bytes above which memory-mapped files are used.
    /// Default is 10MB.
    /// </summary>
    public long MemoryMappedThresholdBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the buffer size in bytes for file I/O operations.
    /// Default is 81920 (80KB).
    /// </summary>
    public int BufferSize { get; set; } = 81920;

    /// <summary>
    /// Gets or sets whether to validate data integrity on read using checksums.
    /// Default is false (for performance).
    /// </summary>
    public bool ValidateChecksums { get; set; }

    /// <summary>
    /// Gets or sets the default timeframes to store when saving data without explicit TargetTimeframes.
    /// If null, data is stored in its original timeframe(s).
    /// </summary>
    public CandlestickInterval[]? DefaultTimeframes { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically create parent directories when saving.
    /// Default is true.
    /// </summary>
    public bool AutoCreateDirectories { get; set; } = true;

    /// <summary>
    /// Creates default options for high-performance scenarios.
    /// </summary>
    /// <returns>A new <see cref="HistoryVaultOptions"/> optimized for performance.</returns>
    public static HistoryVaultOptions HighPerformance => new()
    {
        UseMemoryMappedFiles = true,
        ValidateChecksums = false,
        BufferSize = 256 * 1024,
        MaxParallelism = Environment.ProcessorCount * 2
    };

    /// <summary>
    /// Creates default options for reliability-focused scenarios.
    /// </summary>
    /// <returns>A new <see cref="HistoryVaultOptions"/> optimized for data integrity.</returns>
    public static HistoryVaultOptions Reliable => new()
    {
        UseMemoryMappedFiles = false,
        ValidateChecksums = true,
        BufferSize = 81920
    };
}
