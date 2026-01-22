using FluentAssertions;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Storage;

public class HistoryVaultStorageTests : IDisposable
{
    private readonly string _tempPath;
    private readonly HistoryVaultOptions _options;
    private readonly HistoryVaultStorage _vault;

    public HistoryVaultStorageTests()
    {
        _tempPath = TestHelpers.GetTempDirectory();
        _options = new HistoryVaultOptions
        {
            BasePathOverride = _tempPath,
            DefaultScope = StorageScope.Local
        };
        _vault = new HistoryVaultStorage(_options);
    }

    public void Dispose()
    {
        _vault.DisposeAsync().AsTask().GetAwaiter().GetResult();
        TestHelpers.CleanupTempDirectory(_tempPath);
    }

    [Fact]
    public async Task Save_WithCompression_CreatesGzFile()
    {
        // Arrange
        var symbolData = TestHelpers.GenerateSymbolData(
            "BTCUSDT",
            CandlestickInterval.M1,
            100,
            new DateTime(2025, 1, 15, 10, 0, 0));

        var options = new SaveOptions
        {
            UseCompression = true,
            Scope = StorageScope.Local
        };

        // Act
        await _vault.SaveAsync(symbolData, options);

        // Assert
        var expectedPath = Path.Combine(_tempPath, "BTCUSDT", "1m", "2025", "01.bin.gz");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_WithoutCompression_CreatesBinFile()
    {
        // Arrange
        var symbolData = TestHelpers.GenerateSymbolData(
            "ETHUSDT",
            CandlestickInterval.H1,
            24,
            new DateTime(2025, 2, 1, 0, 0, 0));

        var options = new SaveOptions
        {
            UseCompression = false,
            Scope = StorageScope.Local
        };

        // Act
        await _vault.SaveAsync(symbolData, options);

        // Assert
        var expectedPath = Path.Combine(_tempPath, "ETHUSDT", "1h", "2025", "02.bin");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_PartialOverwrite_PreservesNonOverlappingData()
    {
        // Arrange
        var symbol = "PARTIAL_TEST";

        // First save: Jan 1-15
        var firstData = TestHelpers.GenerateSymbolData(
            symbol, CandlestickInterval.M1, 1440 * 15,
            new DateTime(2025, 1, 1, 0, 0, 0));
        await _vault.SaveAsync(firstData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        // Get count of original candles
        var originalCount = firstData.Timeframes[0].Candlesticks.Count;

        // Second save: Jan 10-20 (overlaps Jan 10-15)
        var secondData = TestHelpers.GenerateSymbolData(
            symbol, CandlestickInterval.M1, 1440 * 10,
            new DateTime(2025, 1, 10, 0, 0, 0));
        await _vault.SaveAsync(secondData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        // Act
        var loadOptions = LoadOptions.ForSymbol(symbol, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), CandlestickInterval.M1);
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        var candles = result!.Timeframes[0].Candlesticks;

        // Should have candles from Jan 1 (preserved) + new Jan 10-20
        candles.Should().NotBeEmpty();
        candles[0].OpenTime.Should().BeOnOrAfter(new DateTime(2025, 1, 1));
    }

    [Fact]
    public async Task Save_PartialOverwrite_ReplacesOverlappingData()
    {
        // Arrange
        var symbol = "OVERLAP_TEST";
        var startTime = new DateTime(2025, 3, 1, 0, 0, 0);

        // First save
        var firstData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        var originalFirstCandle = firstData.Timeframes[0].Candlesticks[0].Clone();
        await _vault.SaveAsync(firstData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        // Create new data with different values at the same time
        var newData = new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>
            {
                new TimeframeV2
                {
                    Timeframe = CandlestickInterval.H1,
                    Candlesticks = new List<CandlestickV2>
                    {
                        new()
                        {
                            OpenTime = startTime,
                            CloseTime = startTime.AddHours(1).AddTicks(-1),
                            Open = 999m,
                            High = 1000m,
                            Low = 998m,
                            Close = 999.5m,
                            Volume = 12345m
                        }
                    }
                }
            }
        };

        // Act
        await _vault.SaveAsync(newData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        var loadOptions = LoadOptions.ForSymbol(symbol, startTime, startTime.AddDays(1), CandlestickInterval.H1);
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        var firstCandle = result!.Timeframes[0].Candlesticks.First(c => c.OpenTime == startTime);
        firstCandle.Open.Should().Be(999m);  // New data, not original
        firstCandle.Volume.Should().Be(12345m);
    }

    [Fact]
    public async Task Load_WithWarmup_ReturnsExtraCandles()
    {
        // Arrange
        var symbol = "WARMUP_TEST";
        var startTime = new DateTime(2025, 4, 1, 0, 0, 0);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 48, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Request data starting at hour 24, with 10 warmup candles
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime.AddHours(24),
            EndDate = startTime.AddHours(48),
            WarmupCandlesCount = 10,
            Timeframes = new[] { CandlestickInterval.H1 }
        };

        // Act
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        var candles = result!.Timeframes[0].Candlesticks;

        // Should include warmup candles before the start date
        candles.Any(c => c.OpenTime < loadOptions.StartDate).Should().BeTrue();
    }

    [Fact]
    public async Task Load_WildcardSymbol_ReturnsAllMatches()
    {
        // Arrange
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("BTC.USD", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("BTC.EUR", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("ETH.USD", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        var loadOptions = new LoadOptions
        {
            Symbol = "BTC.*",
            Timeframes = new[] { CandlestickInterval.M1 }
        };

        // Act
        var results = await _vault.LoadMultipleAsync(loadOptions);

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Symbol).Should().BeEquivalentTo(new[] { "BTC.USD", "BTC.EUR" });
    }

    [Fact]
    public async Task Load_WithAggregation_CreatesRequestedTimeframe()
    {
        // Arrange
        var symbol = "AGG_TEST";
        var startTime = new DateTime(2025, 5, 1, 0, 0, 0);

        // Save M1 data (60 candles = 1 hour)
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 60, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Request H1 data with aggregation enabled
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            Timeframes = new[] { CandlestickInterval.H1 },
            StartDate = startTime,
            EndDate = startTime.AddHours(1),
            AllowAggregation = true
        };

        // Act
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Timeframes.Should().HaveCount(1);
        result.Timeframes[0].Timeframe.Should().Be(CandlestickInterval.H1);
        result.Timeframes[0].Candlesticks.Should().HaveCount(1);
    }

    [Fact]
    public async Task Load_NonExistentSymbol_ReturnsNull()
    {
        // Arrange
        var loadOptions = new LoadOptions
        {
            Symbol = "NON_EXISTENT_SYMBOL",
            Timeframes = new[] { CandlestickInterval.M1 }
        };

        // Act
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAvailability_ReturnsReport()
    {
        // Arrange
        var symbol = "AVAIL_REPORT";
        var startTime = new DateTime(2025, 6, 1, 0, 0, 0);
        var endTime = startTime.AddHours(24);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, startTime, endTime);

        // Assert - verify report is returned and has correct basic properties
        report.Should().NotBeNull();
        report.Symbol.Should().Be(symbol);
        report.Timeframe.Should().Be(CandlestickInterval.H1);
        report.QueryStart.Should().Be(startTime);
        report.QueryEnd.Should().Be(endTime);
    }

    [Fact]
    public async Task GetMatchingSymbols_ReturnsCorrectSymbols()
    {
        // Arrange
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("TEST.A", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("TEST.B", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("OTHER.A", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        // Act
        var results = await _vault.GetMatchingSymbolsAsync("TEST.*");

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain("TEST.A");
        results.Should().Contain("TEST.B");
    }

    [Fact]
    public async Task DeleteSymbol_RemovesAllData()
    {
        // Arrange
        var symbol = "DELETE_TEST";
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 5, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        // Act
        var deleted = await _vault.DeleteSymbolAsync(symbol, StorageScope.Local);

        // Assert
        deleted.Should().BeTrue();
        var loadResult = await _vault.LoadAsync(new LoadOptions { Symbol = symbol });
        loadResult.Should().BeNull();
    }

    [Fact]
    public async Task GetAvailableTimeframes_ReturnsAllTimeframes()
    {
        // Arrange
        var symbol = "TF_TEST";
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M5, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        // Act
        var timeframes = await _vault.GetAvailableTimeframesAsync(symbol);

        // Assert
        timeframes.Should().HaveCount(3);
        timeframes.Should().Contain(CandlestickInterval.M1);
        timeframes.Should().Contain(CandlestickInterval.M5);
        timeframes.Should().Contain(CandlestickInterval.H1);
    }
}
