using HistoryVault.Models;

namespace HistoryVault.Tests;

public static class TestHelpers
{
    public static List<CandlestickV2> GenerateCandles(
        int count,
        CandlestickInterval interval,
        DateTime startTime,
        decimal basePrice = 100m)
    {
        var candles = new List<CandlestickV2>(count);
        var duration = interval == CandlestickInterval.Tick
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromSeconds((int)interval);

        var random = new Random(42); // Fixed seed for reproducibility
        decimal currentPrice = basePrice;

        for (int i = 0; i < count; i++)
        {
            decimal change = (decimal)(random.NextDouble() - 0.5) * 2; // -1 to 1
            decimal open = currentPrice;
            decimal close = currentPrice + change;
            decimal high = Math.Max(open, close) + (decimal)random.NextDouble() * 0.5m;
            decimal low = Math.Min(open, close) - (decimal)random.NextDouble() * 0.5m;
            decimal volume = (decimal)(random.NextDouble() * 1000 + 100);

            var openTime = startTime.Add(duration * i);
            var closeTime = openTime.Add(duration).AddTicks(-1);

            candles.Add(new CandlestickV2
            {
                OpenTime = openTime,
                CloseTime = closeTime,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });

            currentPrice = close;
        }

        return candles;
    }

    public static SymbolDataV2 GenerateSymbolData(
        string symbol,
        CandlestickInterval interval,
        int candleCount,
        DateTime startTime)
    {
        return new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>
            {
                new TimeframeV2
                {
                    Timeframe = interval,
                    Candlesticks = GenerateCandles(candleCount, interval, startTime)
                }
            }
        };
    }

    /// <summary>
    /// All standard timeframes for multi-timeframe generation.
    /// Note: MN1 is excluded because its short code "1M" collides with M1's "1m"
    /// on case-insensitive filesystems (macOS default, Windows).
    /// </summary>
    public static readonly CandlestickInterval[] AllTimeframes =
    [
        CandlestickInterval.M1,
        CandlestickInterval.M5,
        CandlestickInterval.M15,
        CandlestickInterval.M30,
        CandlestickInterval.H1,
        CandlestickInterval.H4,
        CandlestickInterval.D1,
        CandlestickInterval.W1
    ];

    /// <summary>
    /// All timeframes including MN1. Only use on case-sensitive filesystems
    /// or when M1 is not included in the same test.
    /// </summary>
    public static readonly CandlestickInterval[] AllTimeframesIncludingMonthly =
    [
        CandlestickInterval.M1,
        CandlestickInterval.M5,
        CandlestickInterval.M15,
        CandlestickInterval.M30,
        CandlestickInterval.H1,
        CandlestickInterval.H4,
        CandlestickInterval.D1,
        CandlestickInterval.W1,
        CandlestickInterval.MN1
    ];

    /// <summary>
    /// Generates SymbolDataV2 with multiple timeframes (M1, M5, M15, M30, H1, H4, D1, W1, MN1).
    /// Each timeframe gets the specified number of candles.
    /// </summary>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="candleCountPerTimeframe">Number of candles to generate for each timeframe.</param>
    /// <param name="startTime">The start time for candle generation.</param>
    /// <param name="basePrice">The base price for candle generation.</param>
    /// <returns>SymbolDataV2 with all timeframes populated.</returns>
    public static SymbolDataV2 GenerateMultiTimeframeSymbolData(
        string symbol,
        int candleCountPerTimeframe,
        DateTime startTime,
        decimal basePrice = 100m)
    {
        return GenerateMultiTimeframeSymbolData(symbol, candleCountPerTimeframe, startTime, basePrice, AllTimeframes);
    }

    /// <summary>
    /// Maximum time span for generated data (15 years).
    /// This ensures all timeframes fit within a reasonable date range for testing.
    /// </summary>
    private static readonly TimeSpan MaxDataTimeSpan = TimeSpan.FromDays(15 * 365);

    /// <summary>
    /// Generates SymbolDataV2 with specified timeframes.
    /// Each timeframe gets the specified number of candles, capped to fit within 15 years.
    /// </summary>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="candleCountPerTimeframe">Number of candles to generate for each timeframe.</param>
    /// <param name="startTime">The start time for candle generation.</param>
    /// <param name="basePrice">The base price for candle generation.</param>
    /// <param name="timeframes">The timeframes to generate.</param>
    /// <returns>SymbolDataV2 with specified timeframes populated.</returns>
    public static SymbolDataV2 GenerateMultiTimeframeSymbolData(
        string symbol,
        int candleCountPerTimeframe,
        DateTime startTime,
        decimal basePrice,
        params CandlestickInterval[] timeframes)
    {
        var symbolData = new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>(timeframes.Length)
        };

        foreach (var interval in timeframes)
        {
            // Calculate the actual candle count, capped to fit within MaxDataTimeSpan
            int actualCandleCount = GetCappedCandleCount(candleCountPerTimeframe, interval);

            // Use a different seed for each timeframe to get varied but reproducible data
            int seed = 42 + (int)interval;
            var candles = GenerateCandlesWithSeed(actualCandleCount, interval, startTime, basePrice, seed);

            symbolData.Timeframes.Add(new TimeframeV2
            {
                Timeframe = interval,
                Candlesticks = candles
            });
        }

        return symbolData;
    }

    /// <summary>
    /// Calculates the candle count capped to fit within the maximum time span.
    /// </summary>
    private static int GetCappedCandleCount(int requestedCount, CandlestickInterval interval)
    {
        if (interval == CandlestickInterval.Tick || interval == CandlestickInterval.Custom)
        {
            return requestedCount;
        }

        var intervalDuration = TimeSpan.FromSeconds((int)interval);
        int maxCandlesInTimeSpan = (int)(MaxDataTimeSpan.TotalSeconds / intervalDuration.TotalSeconds);

        return Math.Min(requestedCount, maxCandlesInTimeSpan);
    }

    /// <summary>
    /// Generates candles with a specific random seed for reproducibility.
    /// </summary>
    public static List<CandlestickV2> GenerateCandlesWithSeed(
        int count,
        CandlestickInterval interval,
        DateTime startTime,
        decimal basePrice,
        int seed)
    {
        var candles = new List<CandlestickV2>(count);
        var duration = interval == CandlestickInterval.Tick
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromSeconds((int)interval);

        var random = new Random(seed);
        decimal currentPrice = basePrice;

        for (int i = 0; i < count; i++)
        {
            decimal change = (decimal)(random.NextDouble() - 0.5) * 2;
            decimal open = currentPrice;
            decimal close = currentPrice + change;
            decimal high = Math.Max(open, close) + (decimal)random.NextDouble() * 0.5m;
            decimal low = Math.Min(open, close) - (decimal)random.NextDouble() * 0.5m;
            decimal volume = (decimal)(random.NextDouble() * 1000 + 100);

            var openTime = startTime.Add(duration * i);
            var closeTime = openTime.Add(duration).AddTicks(-1);

            candles.Add(new CandlestickV2
            {
                OpenTime = openTime,
                CloseTime = closeTime,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });

            currentPrice = close;
        }

        return candles;
    }

    public static string GetTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HistoryVault.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    public static void CleanupTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
