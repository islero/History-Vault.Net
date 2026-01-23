# HistoryVault.Net

A high-performance market data storage library for .NET HFT/HPC systems. Store, load, and aggregate OHLCV candlestick data with optimal memory efficiency and minimal allocations.

## Features

- **High-Performance Binary Serialization**: Optimized for HFT/HPC with Span<byte>, ArrayPool, and zero-allocation patterns
- **GZip Compression**: Optional compression with configurable levels
- **Cross-Platform Storage**: Automatic platform-specific path resolution for Windows, macOS, and Linux
- **Timeframe Aggregation**: Aggregate candlesticks from smaller to larger timeframes (M1 → M5 → H1 → D1)
- **Partial Overwrite**: Merge new data with existing data, preserving non-overlapping candles
- **Wildcard Symbol Matching**: Glob pattern support for symbol lookup (e.g., `BTC.*`, `*.USD`)
- **Data Availability Reporting**: Check data coverage and identify gaps
- **Warmup Candles**: Load extra candles before requested time range for indicator calculations
- **Thread-Safe Operations**: Built-in synchronization for concurrent access

## Installation

```bash
dotnet add package HistoryVault
```

Or via NuGet Package Manager:

```
Install-Package HistoryVault
```

## Quick Start

### Basic Usage

```csharp
using HistoryVault;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;

// Configure the vault (paths are auto-detected based on OS and scope)
var options = new HistoryVaultOptions
{
    DefaultScope = StorageScope.Local  // Data stored in ./data/history-vault/
};

await using var vault = new HistoryVaultStorage(options);

// Save candlestick data
var symbolData = new SymbolDataV2
{
    Symbol = "BTCUSDT",
    Timeframes = new List<TimeframeV2>
    {
        new TimeframeV2
        {
            Timeframe = CandlestickInterval.M1,
            Candlesticks = candlesticks // Your candlestick list
        }
    }
};

await vault.SaveAsync(symbolData, new SaveOptions
{
    UseCompression = true,
    AllowPartialOverwrite = true
});

// Load candlestick data
var loadOptions = LoadOptions.ForSymbol(
    "BTCUSDT",
    new DateTime(2025, 1, 1),
    new DateTime(2025, 1, 31),
    CandlestickInterval.M1
);

var data = await vault.LoadAsync(loadOptions);
```

### Timeframe Aggregation

```csharp
// Load M1 data and aggregate to H1 on-the-fly
var loadOptions = new LoadOptions
{
    Symbol = "BTCUSDT",
    StartDate = new DateTime(2025, 1, 1),
    EndDate = new DateTime(2025, 1, 31),
    Timeframes = new[] { CandlestickInterval.H1 },
    AllowAggregation = true  // Aggregates from M1 if H1 not available
};

var data = await vault.LoadAsync(loadOptions);
```

### Wildcard Symbol Matching

```csharp
// Load all BTC pairs
var loadOptions = new LoadOptions
{
    Symbol = "BTC.*",  // Matches BTC.USD, BTC.EUR, etc.
    Timeframes = new[] { CandlestickInterval.M1 }
};

var allBtcData = await vault.LoadMultipleAsync(loadOptions);

// Get matching symbol names
var symbols = await vault.GetMatchingSymbolsAsync("*.USD");
```

### Data Availability Check

```csharp
var report = await vault.CheckAvailabilityAsync(
    "BTCUSDT",
    CandlestickInterval.M1,
    new DateTime(2025, 1, 1),
    new DateTime(2025, 12, 31)
);

Console.WriteLine($"Coverage: {report.CoveragePercentage:P2}");
Console.WriteLine($"Available: {report.TotalCandlesAvailable} / {report.ExpectedCandlesCount} candles");

foreach (var gap in report.Gaps)
{
    Console.WriteLine($"Gap: {gap.Start} - {gap.End}");
}
```

### Warmup Candles for Indicators

```csharp
var loadOptions = new LoadOptions
{
    Symbol = "BTCUSDT",
    StartDate = new DateTime(2025, 1, 15),
    EndDate = new DateTime(2025, 1, 31),
    Timeframes = new[] { CandlestickInterval.H1 },
    WarmupCandlesCount = 50  // Extra 50 candles before StartDate
};

var data = await vault.LoadAsync(loadOptions);
// Use warmup candles to initialize moving averages, etc.
```

## API Reference

### IHistoryVault Interface

| Method | Description |
|--------|-------------|
| `SaveAsync(SymbolDataV2, SaveOptions, CancellationToken)` | Save candlestick data |
| `LoadAsync(LoadOptions, CancellationToken)` | Load candlestick data for a single symbol |
| `LoadMultipleAsync(LoadOptions, CancellationToken)` | Load data for multiple symbols (wildcard) |
| `CheckAvailabilityAsync(symbol, interval, start, end)` | Check data availability |
| `GetMatchingSymbolsAsync(pattern, CancellationToken)` | Get symbols matching pattern |
| `GetAvailableTimeframesAsync(symbol, CancellationToken)` | Get available timeframes for symbol |
| `DeleteSymbolAsync(symbol, scope, CancellationToken)` | Delete all data for a symbol |

### Configuration Classes

#### HistoryVaultOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultScope` | StorageScope | Local | Default storage scope |
| `BasePathOverride` | string? | null | Override automatic path resolution (for testing) |

**Automatic Path Resolution:**
- **Local scope**: `./data/history-vault/` (relative to current working directory)
- **Global scope**: OS temp directory + `/HistoryVault`
  - Windows: `C:\Users\<user>\AppData\Local\Temp\HistoryVault`
  - macOS: `/var/folders/.../T/HistoryVault`
  - Linux: `/tmp/HistoryVault`

#### SaveOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseCompression` | bool | true | Enable GZip compression |
| `AllowPartialOverwrite` | bool | false | Merge with existing data |
| `CompressionLevel` | CompressionLevel | Optimal | GZip compression level |
| `Scope` | StorageScope | Local | Storage scope |

#### LoadOptions

| Property | Type | Description |
|----------|------|-------------|
| `Symbol` | string | Symbol name or wildcard pattern |
| `StartDate` | DateTime? | Start of date range |
| `EndDate` | DateTime? | End of date range |
| `Timeframes` | CandlestickInterval[] | Timeframes to load |
| `WarmupCandlesCount` | int | Extra candles before StartDate |
| `AllowAggregation` | bool | Aggregate from smaller timeframes |
| `Scope` | StorageScope | Storage scope |

### CandlestickInterval Enum

| Value | Code | Seconds |
|-------|------|---------|
| Tick | tick | 0 |
| Second | 1s | 1 |
| M1 | 1m | 60 |
| M3 | 3m | 180 |
| M5 | 5m | 300 |
| M15 | 15m | 900 |
| M30 | 30m | 1800 |
| H1 | 1h | 3600 |
| H2 | 2h | 7200 |
| H4 | 4h | 14400 |
| H6 | 6h | 21600 |
| H8 | 8h | 28800 |
| H12 | 12h | 43200 |
| D1 | 1d | 86400 |
| D3 | 3d | 259200 |
| W1 | 1w | 604800 |
| MN1 | 1M | 2592000 |

## Storage Format

### Directory Structure

```
{BasePath}/
  {Symbol}/
    {Timeframe}/
      {Year}/
        {Month}.bin[.gz]
```

Example:
```
/data/
  BTCUSDT/
    1m/
      2025/
        01.bin.gz
        02.bin.gz
    1h/
      2025/
        01.bin.gz
```

### Binary Format

- **Header**: 64 bytes (magic number, version, symbol, timeframe, count, date range, checksums)
- **Records**: 96 bytes per candlestick (timestamps, OHLCV as decimals, flags)

## Performance Benchmarks

```
BenchmarkDotNet v0.13.12, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M3 Max, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0, Arm64 RyuJIT AdvSIMD
  .NET 10.0 : .NET 10.0.0, Arm64 RyuJIT AdvSIMD
```

| Method             | CandleCount | Mean           | Error       | StdDev       | Gen0       | Gen1      | Gen2      | Allocated   |
|------------------- |------------ |---------------:|------------:|-------------:|-----------:|----------:|----------:|------------:|
| Save_Uncompressed  | 10000       |       777.3 μs |     9.98 μs |      8.85 μs |    67.3828 |   30.2734 |   17.5781 |    551425 B |
| Save_Compressed    | 10000       |     9,018.2 μs |    59.88 μs |     50.00 μs |    46.8750 |   15.6250 |         - |    634350 B |
| Load_Uncompressed  | 10000       |    17,705.8 μs |   351.18 μs |    468.81 μs |  1000.0000 |  812.5000 |  468.7500 |   7632409 B |
| Load_Compressed    | 10000       |    17,626.3 μs |   299.01 μs |    293.67 μs |  1000.0000 |  750.0000 |  468.7500 |   7631478 B |
| Serialize          | 10000       |       143.9 μs |     1.15 μs |      1.07 μs |          - |         - |         - |           - |
| Aggregate_M1_To_H4 | 10000       |        79.9 μs |     0.13 μs |      0.12 μs |     0.7324 |         - |         - |      7112 B |
| Save_Uncompressed  | 100000      |     5,761.5 μs |    51.96 μs |     48.60 μs |   101.5625 |   46.8750 |   46.8750 |   5183313 B |
| Save_Compressed    | 100000      |    96,664.3 μs |   442.80 μs |    369.76 μs |          - |         - |         - |   5431933 B |
| Load_Uncompressed  | 100000      |    49,808.7 μs |   524.02 μs |    490.17 μs |  2500.0000 | 1700.0000 |  800.0000 |  51547289 B |
| Load_Compressed    | 100000      |    50,045.5 μs |   960.85 μs |  1,180.01 μs |  2444.4444 | 1444.4444 |  777.7778 |  51546065 B |
| Serialize          | 100000      |     1,447.4 μs |    10.46 μs |      9.78 μs |          - |         - |         - |           - |
| Aggregate_M1_To_H4 | 100000      |       869.8 μs |    12.09 μs |     10.10 μs |     5.8594 |         - |         - |     52112 B |
| Save_Uncompressed  | 1000000     |    64,572.0 μs |   896.57 μs |    748.68 μs |   625.0000 |  250.0000 |  250.0000 |  52288857 B |
| Save_Compressed    | 1000000     | 1,036,204.6 μs | 6,169.11 μs |  5,770.59 μs |          - |         - |         - |  54194816 B |
| Load_Uncompressed  | 1000000     |   433,586.9 μs | 3,606.26 μs |  3,373.29 μs | 16000.0000 | 9000.0000 | 2000.0000 | 618089160 B |
| Load_Compressed    | 1000000     |   361,100.2 μs | 2,724.78 μs |  2,275.31 μs | 15000.0000 | 8000.0000 | 1000.0000 | 617956768 B |
| Serialize          | 1000000     |    14,552.7 μs |   100.23 μs |     93.76 μs |          - |         - |         - |           - |
| Aggregate_M1_To_H4 | 1000000     |    10,934.0 μs |   216.22 μs |    231.35 μs |    46.8750 |   15.6250 |         - |    502112 B |

## Aggregation Rules

When aggregating candlesticks to larger timeframes:

- **Open**: First candle's open price
- **High**: Maximum high across all candles
- **Low**: Minimum low across all candles
- **Close**: Last candle's close price
- **Volume**: Sum of all volumes
- **OpenTime**: First candle's open time
- **CloseTime**: Last candle's close time

Valid aggregation paths:
- M1 → M3, M5, M15, M30, H1, H2, H4, H6, H8, H12, D1
- M5 → M15, M30, H1, etc.
- H1 → H2, H4, H6, H8, H12, D1

## Thread Safety

HistoryVaultStorage is thread-safe for concurrent read/write operations. Internal locking ensures data consistency when multiple threads access the same symbol/timeframe.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
