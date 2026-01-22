using BenchmarkDotNet.Attributes;
using HistoryVault.Aggregation;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;

namespace HistoryVault.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class VaultBenchmarks
{
    private string _tempPath = null!;
    private HistoryVaultStorage _vault = null!;
    private List<CandlestickV2> _candles = null!;
    private SymbolDataV2 _symbolData = null!;
    private BinarySerializer _serializer = null!;
    private CandlestickAggregator _aggregator = null!;

    [Params(10_000, 100_000, 1_000_000)]
    public int CandleCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "HVBench", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);

        _vault = new HistoryVaultStorage(new HistoryVaultOptions
        {
            BasePathOverride = _tempPath,
            DefaultScope = StorageScope.Local
        });

        _candles = GenerateCandles(CandleCount);
        _symbolData = new SymbolDataV2
        {
            Symbol = "BENCHMARK",
            Timeframes = new List<TimeframeV2>
            {
                new TimeframeV2
                {
                    Timeframe = CandlestickInterval.M1,
                    Candlesticks = _candles
                }
            }
        };

        _serializer = new BinarySerializer();
        _aggregator = new CandlestickAggregator();

        // Pre-save data for load benchmarks
        _vault.SaveAsync(_symbolData, new SaveOptions { UseCompression = false }).GetAwaiter().GetResult();
        _vault.SaveAsync(_symbolData, new SaveOptions { UseCompression = true }).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _vault.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Benchmark]
    public async Task Save_Uncompressed()
    {
        var symbol = $"BENCH_UNCOMP_{Guid.NewGuid():N}";
        var data = new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>
            {
                new TimeframeV2 { Timeframe = CandlestickInterval.M1, Candlesticks = _candles }
            }
        };

        await _vault.SaveAsync(data, new SaveOptions { UseCompression = false });
    }

    [Benchmark]
    public async Task Save_Compressed()
    {
        var symbol = $"BENCH_COMP_{Guid.NewGuid():N}";
        var data = new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>
            {
                new TimeframeV2 { Timeframe = CandlestickInterval.M1, Candlesticks = _candles }
            }
        };

        await _vault.SaveAsync(data, new SaveOptions { UseCompression = true });
    }

    [Benchmark]
    public async Task Load_Uncompressed()
    {
        var loadOptions = new LoadOptions
        {
            Symbol = "BENCHMARK",
            Timeframes = new[] { CandlestickInterval.M1 }
        };
        await _vault.LoadAsync(loadOptions);
    }

    [Benchmark]
    public async Task Load_Compressed()
    {
        var loadOptions = new LoadOptions
        {
            Symbol = "BENCHMARK",
            Timeframes = new[] { CandlestickInterval.M1 }
        };
        await _vault.LoadAsync(loadOptions);
    }

    [Benchmark]
    public void Serialize()
    {
        var (buffer, _) = _serializer.Serialize(_candles, CandlestickInterval.M1, false);
        _serializer.ReturnBuffer(buffer);
    }

    [Benchmark]
    public IReadOnlyList<CandlestickV2> Aggregate_M1_To_H4()
    {
        return _aggregator.Aggregate(_candles, CandlestickInterval.M1, CandlestickInterval.H4);
    }

    private static List<CandlestickV2> GenerateCandles(int count)
    {
        var candles = new List<CandlestickV2>(count);
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var random = new Random(42);
        decimal price = 100m;

        for (int i = 0; i < count; i++)
        {
            decimal change = (decimal)(random.NextDouble() - 0.5) * 2;
            decimal open = price;
            decimal close = price + change;
            decimal high = Math.Max(open, close) + (decimal)random.NextDouble() * 0.5m;
            decimal low = Math.Min(open, close) - (decimal)random.NextDouble() * 0.5m;

            candles.Add(new CandlestickV2
            {
                OpenTime = startTime.AddMinutes(i),
                CloseTime = startTime.AddMinutes(i + 1).AddTicks(-1),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = (decimal)(random.NextDouble() * 1000 + 100)
            });

            price = close;
        }

        return candles;
    }
}
