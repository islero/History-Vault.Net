using HistoryVault.Models;
using HistoryVault.Storage;

namespace HistoryVault.Indexing;

/// <summary>
/// Provides time range indexing and data availability checking.
/// </summary>
public sealed class TimeRangeIndex
{
    private readonly StoragePathResolver _pathResolver;
    private readonly BinarySerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeRangeIndex"/> class.
    /// </summary>
    /// <param name="pathResolver">The path resolver.</param>
    /// <param name="serializer">The binary serializer.</param>
    public TimeRangeIndex(StoragePathResolver pathResolver, BinarySerializer serializer)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Gets the data bounds (earliest and latest timestamps) for a symbol and timeframe.
    /// </summary>
    /// <param name="symbol">The symbol to query.</param>
    /// <param name="timeframe">The timeframe to query.</param>
    /// <param name="scope">The storage scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of (earliest, latest) timestamps, or null if no data exists.</returns>
    public async Task<(DateTime Earliest, DateTime Latest)?> GetDataBoundsAsync(
        string symbol,
        CandlestickInterval timeframe,
        StorageScope scope,
        CancellationToken ct = default)
    {
        var files = _pathResolver.GetExistingDataFiles(scope, symbol, timeframe).ToList();

        if (files.Count == 0)
        {
            return null;
        }

        DateTime? earliest = null;
        DateTime? latest = null;

        // Read the first file header
        BinarySerializer.HeaderInfo? firstHeader = await ReadFileHeaderAsync(files[0], ct).ConfigureAwait(false);
        if (firstHeader is { RecordCount: > 0 })
        {
            earliest = firstHeader.Value.FirstTimestamp;
        }

        // Read the last file header
        BinarySerializer.HeaderInfo? lastHeader = await ReadFileHeaderAsync(files[^1], ct).ConfigureAwait(false);
        if (lastHeader is { RecordCount: > 0 })
        {
            latest = lastHeader.Value.LastTimestamp;
        }

        if (earliest.HasValue && latest.HasValue)
        {
            return (earliest.Value, latest.Value);
        }

        return null;
    }

    /// <summary>
    /// Checks data availability for a specific date range.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="timeframe">The timeframe to check.</param>
    /// <param name="start">The start of the date range.</param>
    /// <param name="end">The end of the date range.</param>
    /// <param name="scope">The storage scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A data availability report.</returns>
    public async Task<DataAvailabilityReport> CheckAvailabilityAsync(
        string symbol,
        CandlestickInterval timeframe,
        DateTime start,
        DateTime end,
        StorageScope scope,
        CancellationToken ct = default)
    {
        var files = _pathResolver.GetDataFilesInRange(scope, symbol, timeframe, start, end).ToList();
        var availableRanges = new List<DateRange>();
        int totalCandlesAvailable = 0;

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();

            BinarySerializer.HeaderInfo? header = await ReadFileHeaderAsync(file, ct).ConfigureAwait(false);
            if (header == null || header.Value.RecordCount == 0)
            {
                continue;
            }

            DateTime fileStart = header.Value.FirstTimestamp;
            DateTime fileEnd = header.Value.LastTimestamp;

            // Store original file range for candle estimation
            DateTime originalFileStart = fileStart;
            DateTime originalFileEnd = fileEnd;

            // Clamp to the requested range
            if (fileStart < start)
            {
                fileStart = start;
            }
            if (fileEnd > end)
            {
                fileEnd = end;
            }

            if (fileStart <= fileEnd)
            {
                availableRanges.Add(new DateRange(fileStart, fileEnd));

                // Estimate candle count based on proportion of file within queried range
                double originalDuration = (originalFileEnd - originalFileStart).TotalSeconds;
                if (originalDuration > 0)
                {
                    double clampedDuration = (fileEnd - fileStart).TotalSeconds;
                    double proportion = clampedDuration / originalDuration;
                    int estimatedCandles = (int)Math.Ceiling(header.Value.RecordCount * proportion);
                    totalCandlesAvailable += estimatedCandles;
                }
                else
                {
                    // Single-point range or edge case
                    totalCandlesAvailable += header.Value.RecordCount;
                }
            }
        }

        // Merge adjacent ranges
        List<DateRange> mergedRanges = MergeAdjacentRanges(availableRanges);

        // Calculate missing ranges
        List<DateRange> missingRanges = CalculateMissingRanges(mergedRanges, start, end);

        // Calculate expected candle count
        int expectedCandlesCount = 0;
        if (timeframe != CandlestickInterval.Tick && timeframe != CandlestickInterval.Custom)
        {
            TimeSpan duration = end - start;
            int intervalSeconds = (int)timeframe;
            expectedCandlesCount = (int)Math.Ceiling(duration.TotalSeconds / intervalSeconds);
        }

        return new DataAvailabilityReport
        {
            Symbol = symbol,
            Timeframe = timeframe,
            QueryStart = start,
            QueryEnd = end,
            AvailableRanges = mergedRanges,
            MissingRanges = missingRanges,
            TotalCandlesAvailable = totalCandlesAvailable,
            ExpectedCandlesCount = expectedCandlesCount
        };
    }

    /// <summary>
    /// Gets file information for a specific date range.
    /// </summary>
    /// <param name="symbol">The symbol.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <param name="start">The start date.</param>
    /// <param name="end">The end date.</param>
    /// <param name="scope">The storage scope.</param>
    /// <returns>A list of file info with their date ranges.</returns>
    public IReadOnlyList<(string FilePath, int Year, int Month)> GetFilesInRange(
        string symbol,
        CandlestickInterval timeframe,
        DateTime start,
        DateTime end,
        StorageScope scope)
    {
        return _pathResolver.GetDataFilesInRange(scope, symbol, timeframe, start, end)
            .Select(f =>
            {
                var (year, month) = StoragePathResolver.ParseFilePath(f);
                return (f, year, month);
            })
            .ToList();
    }

    private async Task<BinarySerializer.HeaderInfo?> ReadFileHeaderAsync(string filePath, CancellationToken ct)
    {
        try
        {
            bool isCompressed = filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            if (isCompressed)
            {
                // For compressed files, we need to decompress to read the header
                // This is less efficient, but necessary
                var compression = new CompressionHandler();
                byte[] decompressed = await compression.DecompressFromStreamAsync(fileStream, ct).ConfigureAwait(false);
                (_, BinarySerializer.HeaderInfo header) = _serializer.Deserialize(decompressed.AsSpan());
                return header;
            }
            else
            {
                byte[] headerBuffer = new byte[BinarySerializer.HeaderSize];
                int bytesRead = await fileStream.ReadAsync(headerBuffer, ct).ConfigureAwait(false);

                if (bytesRead < BinarySerializer.HeaderSize)
                {
                    return null;
                }

                return _serializer.DeserializeHeaderOnly(headerBuffer.AsSpan());
            }
        }
        catch
        {
            return null;
        }
    }

    private static List<DateRange> MergeAdjacentRanges(List<DateRange> ranges)
    {
        if (ranges.Count <= 1)
        {
            return ranges;
        }

        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<DateRange>();
        DateRange current = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            if (current.Overlaps(sorted[i]) || current.IsAdjacentTo(sorted[i]))
            {
                current = current.Merge(sorted[i]);
            }
            else
            {
                merged.Add(current);
                current = sorted[i];
            }
        }

        merged.Add(current);
        return merged;
    }

    private static List<DateRange> CalculateMissingRanges(List<DateRange> availableRanges, DateTime start, DateTime end)
    {
        if (availableRanges.Count == 0)
        {
            return [new DateRange(start, end)];
        }

        var missing = new List<DateRange>();

        // Check the gap at the beginning
        if (availableRanges[0].Start > start)
        {
            missing.Add(new DateRange(start, availableRanges[0].Start.AddTicks(-1)));
        }

        // Check gaps between ranges
        for (int i = 0; i < availableRanges.Count - 1; i++)
        {
            // Only report gaps larger than 1 tick (1 tick is expected candle boundary)
            long gapTicks = (availableRanges[i + 1].Start - availableRanges[i].End).Ticks;
            if (gapTicks > 1)
            {
                DateTime gapStart = availableRanges[i].End.AddTicks(1);
                DateTime gapEnd = availableRanges[i + 1].Start.AddTicks(-1);
                missing.Add(new DateRange(gapStart, gapEnd));
            }
        }

        // Check the gap at the end
        if (availableRanges[^1].End < end)
        {
            missing.Add(new DateRange(availableRanges[^1].End.AddTicks(1), end));
        }

        return missing;
    }
}
