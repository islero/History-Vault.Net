using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HistoryVault.Abstractions;
using HistoryVault.Aggregation;
using HistoryVault.Configuration;
using HistoryVault.Extensions;
using HistoryVault.Indexing;
using HistoryVault.Models;

namespace HistoryVault.Storage;

/// <summary>
/// High-performance market data storage implementation.
/// Thread-safe for concurrent read/write operations.
/// </summary>
public sealed class HistoryVaultStorage : IHistoryVault, IDataAvailabilityChecker, IAsyncDisposable
{
    private readonly HistoryVaultOptions _options;
    private readonly StoragePathResolver _pathResolver;
    private readonly BinarySerializer _serializer;
    private readonly CompressionHandler _compression;
    private readonly CandlestickAggregator _aggregator;
    private readonly SymbolIndex _symbolIndex;
    private readonly TimeRangeIndex _timeRangeIndex;
    private readonly ILogger<HistoryVaultStorage> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _symbolLocks = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryVaultStorage"/> class.
    /// </summary>
    /// <param name="options">The configuration options.</param>
    /// <param name="logger">Optional logger instance.</param>
    public HistoryVaultStorage(HistoryVaultOptions? options = null, ILogger<HistoryVaultStorage>? logger = null)
    {
        _options = options ?? new HistoryVaultOptions();
        _logger = logger ?? NullLogger<HistoryVaultStorage>.Instance;

        _pathResolver = new StoragePathResolver(_options);
        _serializer = new BinarySerializer();
        _compression = new CompressionHandler();
        _aggregator = new CandlestickAggregator();
        _symbolIndex = new SymbolIndex(_pathResolver);
        _timeRangeIndex = new TimeRangeIndex(_pathResolver, _serializer);
    }

    /// <inheritdoc />
    public async Task SaveAsync(SymbolDataV2 data, SaveOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(data.Symbol))
        {
            throw new ArgumentException("Symbol cannot be empty.", nameof(data));
        }

        var symbolLock = _symbolLocks.GetOrAdd(data.Symbol, _ => new SemaphoreSlim(1, 1));
        await symbolLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            foreach (var timeframeData in data.Timeframes)
            {
                ct.ThrowIfCancellationRequested();

                var candlesToSave = timeframeData.Candlesticks;
                if (candlesToSave.Count == 0)
                {
                    continue;
                }

                // Determine target timeframes
                var targetTimeframes = DetermineTargetTimeframes(options, timeframeData);

                foreach (var targetTimeframe in targetTimeframes)
                {
                    ct.ThrowIfCancellationRequested();

                    var candles = targetTimeframe == timeframeData.Timeframe
                        ? candlesToSave
                        : _aggregator.Aggregate(candlesToSave, timeframeData.Timeframe, targetTimeframe).ToList();

                    if (candles.Count == 0)
                    {
                        continue;
                    }

                    await SaveTimeframeDataAsync(
                        data.Symbol,
                        targetTimeframe,
                        candles,
                        options,
                        ct).ConfigureAwait(false);
                }
            }

            _symbolIndex.AddSymbolToCache(data.Symbol, options.Scope);
            _logger.LogDebug("Saved data for symbol {Symbol}", data.Symbol);
        }
        finally
        {
            symbolLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SymbolDataV2?> LoadAsync(LoadOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var results = await LoadMultipleAsync(options, ct).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SymbolDataV2>> LoadMultipleAsync(LoadOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var matchingSymbols = await _symbolIndex.GetMatchingSymbolsAsync(
            options.Symbol, options.Scope, ct).ConfigureAwait(false);

        if (matchingSymbols.Count == 0)
        {
            return Array.Empty<SymbolDataV2>();
        }

        var results = new ConcurrentBag<SymbolDataV2>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(matchingSymbols, parallelOptions, async (symbol, token) =>
        {
            var symbolData = await LoadSymbolDataAsync(symbol, options, token).ConfigureAwait(false);
            if (symbolData != null && symbolData.Timeframes.Count > 0)
            {
                results.Add(symbolData);
            }
        }).ConfigureAwait(false);

        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<DataAvailabilityReport> CheckAvailabilityAsync(
        string symbol,
        CandlestickInterval timeframe,
        DateTime start,
        DateTime end,
        CancellationToken ct = default)
    {
        return await _timeRangeIndex.CheckAvailabilityAsync(
            symbol, timeframe, start, end, _options.DefaultScope, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    Task<DataAvailabilityReport> IDataAvailabilityChecker.CheckAvailabilityAsync(
        string symbol,
        CandlestickInterval timeframe,
        DateTime start,
        DateTime end,
        StorageScope scope,
        CancellationToken ct)
    {
        return _timeRangeIndex.CheckAvailabilityAsync(symbol, timeframe, start, end, scope, ct);
    }

    /// <inheritdoc />
    public Task<(DateTime Earliest, DateTime Latest)?> GetDataBoundsAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default)
    {
        return _timeRangeIndex.GetDataBoundsAsync(symbol, timeframe, scope, ct);
    }

    /// <inheritdoc />
    public Task<bool> HasDataAsync(string symbol, StorageScope scope, CancellationToken ct = default)
    {
        return Task.FromResult(_symbolIndex.SymbolExists(symbol, scope));
    }

    /// <inheritdoc />
    public async Task<bool> HasDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default)
    {
        var bounds = await GetDataBoundsAsync(symbol, timeframe, scope, ct).ConfigureAwait(false);
        return bounds.HasValue;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetMatchingSymbolsAsync(string pattern, CancellationToken ct = default)
    {
        return _symbolIndex.GetMatchingSymbolsAsync(pattern, _options.DefaultScope, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CandlestickInterval>> GetAvailableTimeframesAsync(
        string symbol,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<CandlestickInterval>>(
            _symbolIndex.GetAvailableTimeframes(symbol, _options.DefaultScope));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSymbolAsync(string symbol, StorageScope scope, CancellationToken ct = default)
    {
        string symbolPath = _pathResolver.GetSymbolPath(scope, symbol);

        if (!Directory.Exists(symbolPath))
        {
            return false;
        }

        await Task.Run(() => Directory.Delete(symbolPath, recursive: true), ct).ConfigureAwait(false);
        _symbolIndex.InvalidateCache(scope);

        _logger.LogInformation("Deleted all data for symbol {Symbol}", symbol);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTimeframeAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default)
    {
        string timeframePath = _pathResolver.GetTimeframePath(scope, symbol, timeframe);

        if (!Directory.Exists(timeframePath))
        {
            return false;
        }

        await Task.Run(() => Directory.Delete(timeframePath, recursive: true), ct).ConfigureAwait(false);

        _logger.LogInformation("Deleted data for symbol {Symbol}, timeframe {Timeframe}", symbol, timeframe);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();

        foreach (var lockItem in _symbolLocks.Values)
        {
            lockItem.Dispose();
        }

        _symbolLocks.Clear();
        await Task.CompletedTask;
    }

    private async Task SaveTimeframeDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        IReadOnlyList<CandlestickV2> candles,
        SaveOptions options,
        CancellationToken ct)
    {
        // Group candles by year/month
        var groupedByMonth = candles
            .GroupBy(c => (c.OpenTime.Year, c.OpenTime.Month))
            .OrderBy(g => g.Key);

        foreach (var monthGroup in groupedByMonth)
        {
            ct.ThrowIfCancellationRequested();

            var (year, month) = monthGroup.Key;
            var monthCandles = monthGroup.OrderBy(c => c.OpenTime).ToList();

            if (options.AllowPartialOverwrite)
            {
                monthCandles = await MergeWithExistingDataAsync(
                    symbol, timeframe, year, month, monthCandles, options, ct).ConfigureAwait(false);
            }

            await WriteMonthDataAsync(
                symbol, timeframe, year, month, monthCandles, options, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<CandlestickV2>> MergeWithExistingDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        int year,
        int month,
        List<CandlestickV2> newCandles,
        SaveOptions options,
        CancellationToken ct)
    {
        // Try to load existing data
        string compressedPath = _pathResolver.GetMonthFilePath(options.Scope, symbol, timeframe, year, month, true);
        string uncompressedPath = _pathResolver.GetMonthFilePath(options.Scope, symbol, timeframe, year, month, false);

        string? existingPath = File.Exists(compressedPath) ? compressedPath :
                               File.Exists(uncompressedPath) ? uncompressedPath : null;

        if (existingPath == null)
        {
            return newCandles;
        }

        var existingCandles = await LoadFileDataAsync(existingPath, ct).ConfigureAwait(false);

        if (existingCandles.Count == 0)
        {
            return newCandles;
        }

        var newStart = newCandles[0].OpenTime;
        var newEnd = newCandles[^1].OpenTime;

        // Keep candles before the new data range
        var preserved = existingCandles.Where(c => c.OpenTime < newStart).ToList();

        // Add new candles (they replace any overlap)
        preserved.AddRange(newCandles);

        // Add candles after the new data range that weren't replaced
        var afterNew = existingCandles.Where(c => c.OpenTime > newEnd);
        preserved.AddRange(afterNew);

        // Sort and deduplicate
        return preserved
            .OrderBy(c => c.OpenTime)
            .GroupBy(c => c.OpenTime)
            .Select(g => g.Last())
            .ToList();
    }

    private async Task WriteMonthDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        int year,
        int month,
        IReadOnlyList<CandlestickV2> candles,
        SaveOptions options,
        CancellationToken ct)
    {
        string filePath = _pathResolver.GetMonthFilePath(
            options.Scope, symbol, timeframe, year, month, options.UseCompression);

        if (_options.AutoCreateDirectories)
        {
            _pathResolver.EnsureDirectoryExists(filePath);
        }

        var (buffer, length) = _serializer.Serialize(candles, timeframe, options.UseCompression);

        try
        {
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: _options.BufferSize,
                useAsync: true);

            if (options.UseCompression)
            {
                await _compression.CompressToStreamAsync(
                    buffer.AsMemory(0, length),
                    fileStream,
                    options.CompressionLevel,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
            }

            await fileStream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _serializer.ReturnBuffer(buffer);
        }

        // Remove old file with different compression setting
        string otherPath = _pathResolver.GetMonthFilePath(
            options.Scope, symbol, timeframe, year, month, !options.UseCompression);

        if (File.Exists(otherPath))
        {
            File.Delete(otherPath);
        }
    }

    private async Task<SymbolDataV2?> LoadSymbolDataAsync(
        string symbol,
        LoadOptions options,
        CancellationToken ct)
    {
        var availableTimeframes = _symbolIndex.GetAvailableTimeframes(symbol, options.Scope);
        var requestedTimeframes = options.Timeframes ?? availableTimeframes.ToArray();

        if (requestedTimeframes.Length == 0)
        {
            requestedTimeframes = availableTimeframes.ToArray();
        }

        var symbolData = new SymbolDataV2 { Symbol = symbol };

        foreach (var timeframe in requestedTimeframes)
        {
            ct.ThrowIfCancellationRequested();

            var candles = await LoadTimeframeDataAsync(
                symbol, timeframe, options, ct).ConfigureAwait(false);

            if (candles.Count > 0)
            {
                symbolData.Timeframes.Add(new TimeframeV2
                {
                    Timeframe = timeframe,
                    Candlesticks = candles,
                    StartIndex = 0,
                    Index = 0,
                    EndIndex = candles.Count - 1
                });
            }
            else if (options.AllowAggregation)
            {
                // Try to aggregate from a smaller available timeframe
                candles = await TryAggregateTimeframeAsync(
                    symbol, timeframe, availableTimeframes, options, ct).ConfigureAwait(false);

                if (candles.Count > 0)
                {
                    symbolData.Timeframes.Add(new TimeframeV2
                    {
                        Timeframe = timeframe,
                        Candlesticks = candles,
                        StartIndex = 0,
                        Index = 0,
                        EndIndex = candles.Count - 1
                    });
                }
            }
        }

        return symbolData.Timeframes.Count > 0 ? symbolData : null;
    }

    private async Task<List<CandlestickV2>> LoadTimeframeDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        LoadOptions options,
        CancellationToken ct)
    {
        var startDate = options.StartDate ?? DateTime.MinValue;
        var endDate = options.EndDate ?? DateTime.MaxValue;

        // Adjust start date for warmup
        if (options.WarmupCandlesCount > 0 && timeframe != CandlestickInterval.Tick)
        {
            var warmupDuration = timeframe.ToTimeSpan() * options.WarmupCandlesCount;
            startDate = startDate.Subtract(warmupDuration);
        }

        var files = _pathResolver.GetDataFilesInRange(
            options.Scope, symbol, timeframe, startDate, endDate).ToList();

        if (files.Count == 0)
        {
            return new List<CandlestickV2>();
        }

        var allCandles = new List<CandlestickV2>();

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileCandles = await LoadFileDataAsync(file, ct).ConfigureAwait(false);
            allCandles.AddRange(fileCandles);
        }

        // Filter to requested date range
        var originalStart = options.StartDate ?? DateTime.MinValue;
        var originalEnd = options.EndDate ?? DateTime.MaxValue;

        var filtered = allCandles
            .Where(c => c.OpenTime >= startDate && c.OpenTime <= originalEnd)
            .OrderBy(c => c.OpenTime)
            .ToList();

        return filtered;
    }

    private async Task<List<CandlestickV2>> LoadFileDataAsync(string filePath, CancellationToken ct)
    {
        bool isCompressed = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: _options.BufferSize,
            useAsync: true);

        if (isCompressed)
        {
            var decompressed = await _compression.DecompressFromStreamAsync(fileStream, ct).ConfigureAwait(false);
            var (candles, _) = _serializer.Deserialize(decompressed.AsSpan());
            return candles.ToList();
        }
        else
        {
            var (candles, _) = await _serializer.DeserializeFromStreamAsync(fileStream, ct).ConfigureAwait(false);
            return candles.ToList();
        }
    }

    private async Task<List<CandlestickV2>> TryAggregateTimeframeAsync(
        string symbol,
        CandlestickInterval targetTimeframe,
        IReadOnlyList<CandlestickInterval> availableTimeframes,
        LoadOptions options,
        CancellationToken ct)
    {
        // Find the smallest available timeframe that can be aggregated to target
        var sourceTimeframe = availableTimeframes
            .Where(tf => _aggregator.CanAggregate(tf, targetTimeframe))
            .OrderBy(tf => (int)tf)
            .FirstOrDefault();

        if (sourceTimeframe == default)
        {
            return new List<CandlestickV2>();
        }

        var sourceOptions = new LoadOptions
        {
            Symbol = options.Symbol,
            Timeframes = new[] { sourceTimeframe },
            StartDate = options.StartDate,
            EndDate = options.EndDate,
            WarmupCandlesCount = 0,
            Scope = options.Scope,
            AllowAggregation = false
        };

        var sourceCandles = await LoadTimeframeDataAsync(
            symbol, sourceTimeframe, sourceOptions, ct).ConfigureAwait(false);

        if (sourceCandles.Count == 0)
        {
            return new List<CandlestickV2>();
        }

        var aggregated = _aggregator.Aggregate(sourceCandles, sourceTimeframe, targetTimeframe);
        return aggregated.ToList();
    }

    private CandlestickInterval[] DetermineTargetTimeframes(SaveOptions options, TimeframeV2 sourceData)
    {
        if (options.TargetTimeframes != null && options.TargetTimeframes.Length > 0)
        {
            if (options.AggregateFromSmallest)
            {
                // Include source timeframe and all larger ones that can be aggregated
                var result = new List<CandlestickInterval> { sourceData.Timeframe };
                result.AddRange(options.TargetTimeframes
                    .Where(tf => _aggregator.CanAggregate(sourceData.Timeframe, tf)));
                return result.Distinct().ToArray();
            }

            return options.TargetTimeframes;
        }

        if (_options.DefaultTimeframes != null && _options.DefaultTimeframes.Length > 0)
        {
            return _options.DefaultTimeframes;
        }

        return new[] { sourceData.Timeframe };
    }
}
