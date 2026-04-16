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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _symbolLocks = new(StringComparer.OrdinalIgnoreCase);
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

        StorageScope scope = options.ResolveScope(_options.DefaultScope);
        SemaphoreSlim symbolLock = _symbolLocks.GetOrAdd(data.Symbol, _ => new SemaphoreSlim(1, 1));
        await symbolLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            bool savedAny = false;
            var replacedTimeframes = new HashSet<CandlestickInterval>();

            foreach (TimeframeV2 timeframeData in data.Timeframes)
            {
                ct.ThrowIfCancellationRequested();

                List<CandlestickV2> candlesToSave = timeframeData.Candlesticks;
                if (candlesToSave.Count == 0)
                {
                    continue;
                }

                // Determine target timeframes
                CandlestickInterval[] targetTimeframes = DetermineTargetTimeframes(options, timeframeData);

                foreach (CandlestickInterval targetTimeframe in targetTimeframes)
                {
                    ct.ThrowIfCancellationRequested();

                    List<CandlestickV2> candles = targetTimeframe == timeframeData.Timeframe
                        ? candlesToSave
                        : _aggregator.Aggregate(candlesToSave, timeframeData.Timeframe, targetTimeframe).ToList();

                    if (candles.Count == 0)
                    {
                        continue;
                    }

                    if (!options.AllowPartialOverwrite && replacedTimeframes.Add(targetTimeframe))
                    {
                        await DeleteTimeframeDirectoryIfExistsAsync(
                            data.Symbol,
                            targetTimeframe,
                            scope,
                            ct).ConfigureAwait(false);
                    }

                    await SaveTimeframeDataAsync(
                        data.Symbol,
                        targetTimeframe,
                        candles,
                        scope,
                        options,
                        ct).ConfigureAwait(false);

                    savedAny = true;
                }
            }

            if (savedAny)
            {
                _pathResolver.WriteSymbolMetadata(scope, data.Symbol);
                _symbolIndex.AddSymbolToCache(data.Symbol, scope);
                _logger.LogDebug("Saved data for symbol {Symbol}", data.Symbol);
            }
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

        IReadOnlyList<SymbolDataV2> results = await LoadMultipleAsync(options, ct).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SymbolDataV2>> LoadMultipleAsync(LoadOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        StorageScope scope = options.ResolveScope(_options.DefaultScope);
        IReadOnlyList<string> matchingSymbols = await _symbolIndex.GetMatchingSymbolsAsync(
            options.Symbol, scope, ct).ConfigureAwait(false);

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
            SymbolDataV2? symbolData = await LoadSymbolDataAsync(symbol, options, scope, token).ConfigureAwait(false);
            if (symbolData is { Timeframes.Count: > 0 })
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
        CancellationToken ct) =>
        _timeRangeIndex.CheckAvailabilityAsync(symbol, timeframe, start, end, scope, ct);

    /// <inheritdoc />
    public Task<(DateTime Earliest, DateTime Latest)?> GetDataBoundsAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default) =>
        _timeRangeIndex.GetDataBoundsAsync(symbol, timeframe, scope, ct);

    /// <inheritdoc />
    public Task<bool> HasDataAsync(string symbol, StorageScope scope, CancellationToken ct = default) => Task.FromResult(_symbolIndex.SymbolExists(symbol, scope));

    /// <inheritdoc />
    public async Task<bool> HasDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default)
    {
        (DateTime Earliest, DateTime Latest)? bounds = await GetDataBoundsAsync(symbol, timeframe, scope, ct).ConfigureAwait(false);
        return bounds.HasValue;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetMatchingSymbolsAsync(string pattern, CancellationToken ct = default) => _symbolIndex.GetMatchingSymbolsAsync(pattern, _options.DefaultScope, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<CandlestickInterval>> GetAvailableTimeframesAsync(
        string symbol,
        CancellationToken ct = default)
    {
        return Task.FromResult(
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

        foreach (SemaphoreSlim lockItem in _symbolLocks.Values)
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
        StorageScope scope,
        SaveOptions options,
        CancellationToken ct)
    {
        // Group candles by year/month
        IOrderedEnumerable<IGrouping<(int Year, int Month), CandlestickV2>> groupedByMonth = candles
            .GroupBy(c => (c.OpenTime.Year, c.OpenTime.Month))
            .OrderBy(g => g.Key);

        foreach (IGrouping<(int Year, int Month), CandlestickV2> monthGroup in groupedByMonth)
        {
            ct.ThrowIfCancellationRequested();

            var (year, month) = monthGroup.Key;
            var monthCandles = monthGroup.OrderBy(c => c.OpenTime).ToList();

            if (options.AllowPartialOverwrite)
            {
                monthCandles = await MergeWithExistingDataAsync(
                    symbol, timeframe, year, month, monthCandles, scope, ct).ConfigureAwait(false);
            }

            await WriteMonthDataAsync(
                symbol, timeframe, year, month, monthCandles, scope, options, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<CandlestickV2>> MergeWithExistingDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        int year,
        int month,
        List<CandlestickV2> newCandles,
        StorageScope scope,
        CancellationToken ct)
    {
        // Try to load existing data
        string compressedPath = _pathResolver.GetMonthFilePath(scope, symbol, timeframe, year, month, true);
        string uncompressedPath = _pathResolver.GetMonthFilePath(scope, symbol, timeframe, year, month, false);

        string? existingPath = File.Exists(compressedPath) ? compressedPath :
                               File.Exists(uncompressedPath) ? uncompressedPath : null;

        if (existingPath == null)
        {
            return newCandles;
        }

        List<CandlestickV2> existingCandles;
        try
        {
            existingCandles = await LoadFileDataAsync(existingPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            _logger.LogWarning(ex, "Existing data file {FilePath} could not be read during merge. Replacing it with new data.", existingPath);
            return newCandles;
        }

        if (existingCandles.Count == 0)
        {
            return newCandles;
        }

        var result = new List<CandlestickV2>(existingCandles.Count + newCandles.Count);
        int i = 0; // index for existing candles
        int j = 0; // index for new candles

        // Since both lists are sorted by OpenTime, we can perform a linear merge
        while (i < existingCandles.Count && j < newCandles.Count)
        {
            CandlestickV2 existing = existingCandles[i];
            CandlestickV2 update = newCandles[j];

            if (existing.OpenTime < update.OpenTime)
            {
                result.Add(existing);
                i++;
            }
            else if (existing.OpenTime > update.OpenTime)
            {
                result.Add(update);
                j++;
            }
            else // timestamps are equal, new data overwrites existing
            {
                result.Add(update);
                i++;
                j++;
            }
        }

        // Add remaining existing candles
        while (i < existingCandles.Count)
        {
            result.Add(existingCandles[i]);
            i++;
        }

        // Add remaining new candles
        while (j < newCandles.Count)
        {
            result.Add(newCandles[j]);
            j++;
        }

        return result;
    }

    private async Task WriteMonthDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        int year,
        int month,
        IReadOnlyList<CandlestickV2> candles,
        StorageScope scope,
        SaveOptions options,
        CancellationToken ct)
    {
        string filePath = _pathResolver.GetMonthFilePath(
            scope, symbol, timeframe, year, month, options.UseCompression);

        if (_options.AutoCreateDirectories)
        {
            _pathResolver.EnsureDirectoryExists(filePath);
        }

        var (buffer, length) = _serializer.Serialize(candles, timeframe, options.UseCompression);
        string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: _options.BufferSize,
                useAsync: true))
            {
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

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
        finally
        {
            _serializer.ReturnBuffer(buffer);
        }

        // Remove old file with different compression setting
        string otherPath = _pathResolver.GetMonthFilePath(
            scope, symbol, timeframe, year, month, !options.UseCompression);

        if (File.Exists(otherPath))
        {
            File.Delete(otherPath);
        }
    }

    private async Task DeleteTimeframeDirectoryIfExistsAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct)
    {
        string timeframePath = _pathResolver.GetTimeframePath(scope, symbol, timeframe);

        if (!Directory.Exists(timeframePath))
        {
            return;
        }

        await Task.Run(() => Directory.Delete(timeframePath, recursive: true), ct).ConfigureAwait(false);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Preserve the original write error.
        }
    }

    private async Task<SymbolDataV2?> LoadSymbolDataAsync(
        string symbol,
        LoadOptions options,
        StorageScope scope,
        CancellationToken ct)
    {
        SemaphoreSlim symbolLock = _symbolLocks.GetOrAdd(symbol, _ => new SemaphoreSlim(1, 1));
        await symbolLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            IReadOnlyList<CandlestickInterval> availableTimeframes = _symbolIndex.GetAvailableTimeframes(symbol, scope);
            CandlestickInterval[] requestedTimeframes = options.Timeframes ?? availableTimeframes.ToArray();

            if (requestedTimeframes.Length == 0)
            {
                requestedTimeframes = availableTimeframes.ToArray();
            }

            var symbolData = new SymbolDataV2 { Symbol = symbol };

            foreach (CandlestickInterval timeframe in requestedTimeframes)
            {
                ct.ThrowIfCancellationRequested();

                List<CandlestickV2> candles = await LoadTimeframeDataAsync(
                    symbol, timeframe, options, scope, ct).ConfigureAwait(false);

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
                        symbol, timeframe, availableTimeframes, options, scope, ct).ConfigureAwait(false);

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
        finally
        {
            symbolLock.Release();
        }
    }

    private async Task<List<CandlestickV2>> LoadTimeframeDataAsync(
        string symbol,
        CandlestickInterval timeframe,
        LoadOptions options,
        StorageScope scope,
        CancellationToken ct)
    {
        // Calculate effective dates first
        DateTime originalStart = options.StartDate ?? DateTime.MinValue;
        DateTime originalEnd = options.EndDate ?? DateTime.MaxValue;

        DateTime startDate = originalStart;

        // Adjust start date for warmup
        if (options.StartDate.HasValue && options.WarmupCandlesCount > 0 && CanUseFixedDuration(timeframe))
        {
            TimeSpan warmupDuration = timeframe.ToTimeSpan() * options.WarmupCandlesCount;
            startDate = startDate > DateTime.MinValue.Add(warmupDuration)
                ? startDate.Subtract(warmupDuration)
                : DateTime.MinValue;
        }

        DateTime effectiveEnd = GetEffectiveEndDate(options.EndDate, originalEnd);
        DateTime fileStartDate = GetFileRangeStartDate(startDate, timeframe, options.IncludePartialCandles);

        // Use effectiveEnd when fetching files
        var files = _pathResolver.GetDataFilesInRange(
            scope, symbol, timeframe, fileStartDate, effectiveEnd).ToList();

        if (files.Count == 0)
        {
            return new List<CandlestickV2>();
        }

        var allCandles = new List<CandlestickV2>();

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                List<CandlestickV2> fileCandles = await LoadFileDataAsync(file, ct).ConfigureAwait(false);
                allCandles.AddRange(fileCandles);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                _logger.LogWarning(ex, "Skipping unreadable history file {FilePath}.", file);
            }
        }

        // Filter to the requested date range (using warmup-adjusted startDate and effectiveEnd)
        var filtered = allCandles
            .Where(c => CandleMatchesRange(c, startDate, effectiveEnd, options.IncludePartialCandles))
            .OrderBy(c => c.OpenTime)
            .ToList();

        return filtered;
    }

    private static DateTime GetEffectiveEndDate(DateTime? requestedEndDate, DateTime fallbackEndDate)
    {
        if (!requestedEndDate.HasValue)
        {
            return fallbackEndDate;
        }

        DateTime endDate = requestedEndDate.Value;
        if (endDate == DateTime.MaxValue)
        {
            return endDate;
        }

        return endDate.TimeOfDay == TimeSpan.Zero
            ? endDate.Date.AddDays(1).AddTicks(-1)
            : endDate;
    }

    private static DateTime GetFileRangeStartDate(
        DateTime startDate,
        CandlestickInterval timeframe,
        bool includePartialCandles)
    {
        if (!includePartialCandles || !CanUseFixedDuration(timeframe))
        {
            return startDate;
        }

        TimeSpan duration = timeframe.ToTimeSpan();
        return startDate > DateTime.MinValue.Add(duration)
            ? startDate.Subtract(duration)
            : DateTime.MinValue;
    }

    private static bool CandleMatchesRange(
        CandlestickV2 candle,
        DateTime startDate,
        DateTime endDate,
        bool includePartialCandles)
    {
        return includePartialCandles
            ? candle.OpenTime <= endDate && candle.CloseTime >= startDate
            : candle.OpenTime >= startDate && candle.CloseTime <= endDate;
    }

    private static bool CanUseFixedDuration(CandlestickInterval timeframe) =>
        timeframe != CandlestickInterval.Tick && timeframe != CandlestickInterval.Custom;

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
            byte[] decompressed = await _compression.DecompressFromStreamAsync(fileStream, ct).ConfigureAwait(false);
            (IReadOnlyList<CandlestickV2> candles, _) = _serializer.Deserialize(decompressed.AsSpan());
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
        StorageScope scope,
        CancellationToken ct)
    {
        // Find the smallest available timeframe that can be aggregated to target
        CandlestickInterval sourceTimeframe = availableTimeframes
            .Where(tf => _aggregator.CanAggregate(tf, targetTimeframe))
            .OrderBy(tf => (int)tf)
            .FirstOrDefault();

        if (sourceTimeframe == default)
        {
            return new List<CandlestickV2>();
        }

        int aggregationFactor = sourceTimeframe.GetAggregationFactor(targetTimeframe);
        int sourceWarmup = options.WarmupCandlesCount * aggregationFactor;

        var sourceOptions = new LoadOptions
        {
            Symbol = options.Symbol,
            Timeframes = [sourceTimeframe],
            StartDate = options.StartDate,
            EndDate = options.EndDate,
            WarmupCandlesCount = sourceWarmup,
            Scope = scope,
            AllowAggregation = false
        };

        List<CandlestickV2> sourceCandles = await LoadTimeframeDataAsync(
            symbol, sourceTimeframe, sourceOptions, scope, ct).ConfigureAwait(false);

        if (sourceCandles.Count == 0)
        {
            return new List<CandlestickV2>();
        }

        IReadOnlyList<CandlestickV2> aggregated = _aggregator.Aggregate(sourceCandles, sourceTimeframe, targetTimeframe);
        return aggregated.ToList();
    }

    private CandlestickInterval[] DetermineTargetTimeframes(SaveOptions options, TimeframeV2 sourceData)
    {
        if (options.TargetTimeframes is { Length: > 0 })
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

        if (_options.DefaultTimeframes is { Length: > 0 })
        {
            return _options.DefaultTimeframes;
        }

        return [sourceData.Timeframe];
    }
}
