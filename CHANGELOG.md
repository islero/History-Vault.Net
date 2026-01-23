# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-01-23

### Added

- Initial release of HistoryVault.Net
- NuGet package publishing
- GitHub Actions CI/CD workflows
  - Multi-platform testing (Windows, macOS, Linux)
  - Multi-version testing (.NET 9.0, .NET 10.0)
  - Automated NuGet publishing on release
- High-performance binary serialization with HPC optimizations
  - Span<byte> and ArrayPool<byte> for zero-allocation patterns
  - 64-byte header with metadata and checksums
  - 96-byte records per candlestick
- GZip compression support with configurable compression levels
- Cross-platform storage path resolution
  - Windows: `%LOCALAPPDATA%\HistoryVault`
  - macOS: `~/Library/Application Support/HistoryVault`
  - Linux: `~/.local/share/HistoryVault`
- OHLCV candlestick aggregation
  - Aggregate from smaller to larger timeframes
  - Support for all standard intervals (M1 to MN1)
  - Validation of aggregation compatibility
- Partial overwrite/merge capability
  - Merge new data with existing data
  - Preserve non-overlapping candles
  - Replace overlapping candles with new values
- Wildcard symbol matching
  - Glob patterns (`*` for any, `?` for single character)
  - Pattern examples: `BTC.*`, `*.USD`, `CON.EP.?25`
- Data availability reporting
  - Coverage percentage calculation
  - Gap detection and reporting
  - Expected vs. actual candle counts
- Warmup candles feature
  - Load extra candles before requested range
  - Useful for indicator initialization
- Thread-safe operations
  - SemaphoreSlim for concurrent access
  - Safe parallel read/write operations
- Comprehensive unit test suite (152+ tests)
- BenchmarkDotNet performance benchmarks

### Core Interfaces

- `IHistoryVault` - Main storage interface
- `ICandlestickAggregator` - Timeframe aggregation
- `IDataAvailabilityChecker` - Availability checking

### Models

- `CandlestickV2` - OHLCV candlestick with decimal precision
- `TimeframeV2` - Candlestick collection for a timeframe
- `SymbolDataV2` - Multi-timeframe data container
- `CandlestickInterval` - Timeframe enumeration
- `DateRange` - Date range operations
- `StorageScope` - Local/Global scope
- `DataAvailabilityReport` - Availability report

### Configuration

- `HistoryVaultOptions` - Global configuration
- `SaveOptions` - Save operation options
- `LoadOptions` - Load operation options

## [Unreleased]

### Planned

- Performance optimizations for large datasets
- Memory-mapped file support
- Async streaming for large loads
- Data integrity verification on load
