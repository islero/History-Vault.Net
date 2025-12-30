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
