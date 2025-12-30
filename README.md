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

// Configure the vault
var options = new HistoryVaultOptions
{
    LocalBasePath = "/path/to/data",
    DefaultScope = StorageScope.Local,
    DefaultCompression = true
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
Console.WriteLine($"Available: {report.AvailableCount} / {report.ExpectedCount} candles");

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
| `LocalBasePath` | string | Platform-specific | Local storage directory |
| `GlobalBasePath` | string | Platform-specific | Global shared storage directory |
| `DefaultScope` | StorageScope | Local | Default storage scope |
| `DefaultCompression` | bool | true | Default compression setting |

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
.NET SDK 9.0.304
  [Host]   : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  .NET 9.0 : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
```

| Method             | CandleCount | Mean           | Error       | StdDev       | Gen0       | Gen1      | Gen2      | Allocated   |
|------------------- |------------ |---------------:|------------:|-------------:|-----------:|----------:|----------:|------------:|
| Save_Uncompressed  | 10000       |       827.1 μs |    16.13 μs |     27.82 μs |    64.4531 |   23.4375 |   15.6250 |    551541 B |
| Save_Compressed    | 10000       |     9,928.5 μs |   192.07 μs |    228.64 μs |    46.8750 |         - |         - |    634494 B |
| Load_Uncompressed  | 10000       |    19,826.6 μs |   392.48 μs |    403.05 μs |  1000.0000 |  687.5000 |  468.7500 |   7634136 B |
| Load_Compressed    | 10000       |    20,588.9 μs |   396.30 μs |    580.89 μs |  1031.2500 |  750.0000 |  500.0000 |   7635201 B |
| Serialize          | 10000       |       137.8 μs |     2.75 μs |      2.94 μs |          - |         - |         - |           - |
| Aggregate_M1_To_H4 | 10000       |       117.2 μs |     1.67 μs |      1.56 μs |     0.7324 |         - |         - |      7112 B |
| Save_Uncompressed  | 100000      |     6,106.0 μs |   132.24 μs |    389.91 μs |   156.2500 |  101.5625 |  101.5625 |   5184115 B |
| Save_Compressed    | 100000      |    97,237.3 μs | 1,376.87 μs |  1,149.75 μs |          - |         - |         - |   5432543 B |
| Load_Uncompressed  | 100000      |    46,577.3 μs |   881.47 μs |    824.52 μs |  2363.6364 | 1272.7273 |  727.2727 |  51547470 B |
| Load_Compressed    | 100000      |    48,871.2 μs |   939.84 μs |    923.04 μs |  2363.6364 | 1363.6364 |  727.2727 |  51546647 B |
| Serialize          | 100000      |     1,332.2 μs |     3.35 μs |      3.13 μs |          - |         - |         - |         3 B |
| Aggregate_M1_To_H4 | 100000      |     1,230.1 μs |    16.86 μs |     14.94 μs |     5.8594 |         - |         - |     52115 B |
| Save_Uncompressed  | 1000000     |    61,623.7 μs | 1,171.12 μs |  2,111.78 μs |   666.6667 |  333.3333 |  333.3333 |  52292787 B |
| Save_Compressed    | 1000000     | 1,052,617.1 μs | 7,012.06 μs |  6,559.09 μs |          - |         - |         - |  54198824 B |
| Load_Uncompressed  | 1000000     |   379,540.4 μs | 7,326.86 μs | 11,188.88 μs | 16000.0000 | 9000.0000 | 2000.0000 | 617958624 B |
| Load_Compressed    | 1000000     |   395,842.5 μs | 7,831.83 μs | 12,422.11 μs | 16000.0000 | 9000.0000 | 2000.0000 | 617973144 B |
| Serialize          | 1000000     |    13,696.8 μs |   154.74 μs |    144.74 μs |          - |         - |         - |        17 B |
| Aggregate_M1_To_H4 | 1000000     |    14,421.6 μs |   285.58 μs |    305.57 μs |    46.8750 |   15.6250 |         - |    502129 B |

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
