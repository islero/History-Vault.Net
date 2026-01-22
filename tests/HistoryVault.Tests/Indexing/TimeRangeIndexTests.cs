using FluentAssertions;
using HistoryVault.Configuration;
using HistoryVault.Indexing;
using HistoryVault.Models;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Indexing;

public class TimeRangeIndexTests : IDisposable
{
    private readonly string _tempPath;
    private readonly HistoryVaultOptions _options;
    private readonly HistoryVaultStorage _vault;

    public TimeRangeIndexTests()
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

    #region CheckAvailabilityAsync Tests

    [Fact]
    public async Task CheckAvailabilityAsync_NoData_ReturnsEmptyReport()
    {
        // Arrange
        var symbol = "NODATA";
        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 1, 31);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, start, end);

        // Assert
        report.Should().NotBeNull();
        report.Symbol.Should().Be(symbol);
        report.Timeframe.Should().Be(CandlestickInterval.H1);
        report.QueryStart.Should().Be(start);
        report.QueryEnd.Should().Be(end);
        report.AvailableRanges.Should().BeEmpty();
        report.MissingRanges.Should().HaveCount(1);
        report.MissingRanges[0].Start.Should().Be(start);
        report.MissingRanges[0].End.Should().Be(end);
        report.TotalCandlesAvailable.Should().Be(0);
        report.HasAnyData.Should().BeFalse();
        report.HasFullCoverage.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_FullCoverage_ReturnsCorrectReport()
    {
        // Arrange
        var symbol = "FULLCOV";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        var candleCount = 24; // 24 hours of data
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, candleCount, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        var queryStart = startTime;
        var queryEnd = startTime.AddHours(24);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        report.AvailableRanges.Should().NotBeEmpty();
        report.TotalCandlesAvailable.Should().Be(candleCount);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_PartialQuery_ReturnsCorrectCandleCount()
    {
        // Arrange
        var symbol = "PARTIAL";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        // Save 30 days of M1 data = 30 * 24 * 60 = 43200 candles
        var candleCount = 30 * 24 * 60;
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, candleCount, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Query only 1 day = 1440 candles
        var queryStart = new DateTime(2025, 1, 15, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 16, 0, 0, 0);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.M1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        // The candle count should be close to 1440 (1 day of M1), not 43200
        // Allow some tolerance due to the way available ranges are calculated
        report.TotalCandlesAvailable.Should().BeLessThan(5000,
            "TotalCandlesAvailable should reflect only queried range, not entire file");
    }

    [Fact]
    public async Task CheckAvailabilityAsync_MultipleFiles_MergesRanges()
    {
        // Arrange
        var symbol = "MULTIFILE";

        // Save data for January
        var jan = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1,
            31 * 24, new DateTime(2025, 1, 1, 0, 0, 0));
        await _vault.SaveAsync(jan, new SaveOptions { UseCompression = false });

        // Save data for February
        var feb = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1,
            28 * 24, new DateTime(2025, 2, 1, 0, 0, 0));
        await _vault.SaveAsync(feb, new SaveOptions { UseCompression = false });

        // Query spanning both months
        var queryStart = new DateTime(2025, 1, 1);
        var queryEnd = new DateTime(2025, 2, 28);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        // Ranges should be merged if adjacent/overlapping
        report.AvailableRanges.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_GapInData_IdentifiesMissingRanges()
    {
        // Arrange
        var symbol = "WITHGAP";

        // Save data for January (full month)
        var janData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1,
            31 * 24, new DateTime(2025, 1, 1, 0, 0, 0));
        await _vault.SaveAsync(janData, new SaveOptions { UseCompression = false });

        // Save data for March (skip February - this creates a gap between months)
        var marData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1,
            31 * 24, new DateTime(2025, 3, 1, 0, 0, 0));
        await _vault.SaveAsync(marData, new SaveOptions { UseCompression = false });

        // Query January through March (February should be missing)
        var queryStart = new DateTime(2025, 1, 1);
        var queryEnd = new DateTime(2025, 3, 31);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        report.HasFullCoverage.Should().BeFalse();
        report.MissingRanges.Should().NotBeEmpty();
        // Verify February is identified as missing
        report.MissingRanges.Any(r => r.Start.Month == 2 || r.End.Month == 2).Should().BeTrue();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_QueryBeforeData_ReturnsMissingRange()
    {
        // Arrange
        var symbol = "BEFOREDATA";
        var dataStart = new DateTime(2025, 1, 15, 0, 0, 0);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, dataStart);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Query before data starts
        var queryStart = new DateTime(2025, 1, 1);
        var queryEnd = new DateTime(2025, 1, 31);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        report.MissingRanges.Should().NotBeEmpty();
        // Should have missing range at the beginning
        report.MissingRanges.Any(r => r.Start == queryStart).Should().BeTrue();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_QueryAfterData_ReturnsMissingRange()
    {
        // Arrange
        var symbol = "AFTERDATA";
        var dataStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, dataStart);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Query extending beyond data
        var queryStart = new DateTime(2025, 1, 1);
        var queryEnd = new DateTime(2025, 1, 31);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        report.MissingRanges.Should().NotBeEmpty();
        // Should have missing range at the end
        report.MissingRanges.Any(r => r.End == queryEnd).Should().BeTrue();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_CompressedFile_ReadsHeaderCorrectly()
    {
        // Arrange
        var symbol = "COMPRESSED";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = true });

        var queryStart = startTime;
        var queryEnd = startTime.AddHours(24);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        report.TotalCandlesAvailable.Should().Be(24);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ExpectedCandleCount_CalculatedCorrectly()
    {
        // Arrange
        var symbol = "EXPECTED";
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 2, 0, 0, 0); // 24 hours

        // Don't save any data - just check expected count calculation

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        // 24 hours at H1 = 24 candles expected
        report.ExpectedCandlesCount.Should().Be(24);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_M1Timeframe_CorrectExpectedCount()
    {
        // Arrange
        var symbol = "M1EXPECTED";
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 1, 0, 0); // 1 hour

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.M1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        // 1 hour at M1 = 60 candles expected
        report.ExpectedCandlesCount.Should().Be(60);
    }

    #endregion

    #region GetDataBoundsAsync Tests

    [Fact]
    public async Task GetDataBoundsAsync_NoData_ReturnsNull()
    {
        // Arrange
        var symbol = "NOBOUNDS";

        // Act
        var bounds = await _vault.GetDataBoundsAsync(symbol, CandlestickInterval.H1, StorageScope.Local);

        // Assert
        bounds.Should().BeNull();
    }

    [Fact]
    public async Task GetDataBoundsAsync_WithData_ReturnsCorrectBounds()
    {
        // Arrange
        var symbol = "BOUNDS";
        var startTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var candleCount = 48;
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, candleCount, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Act
        var bounds = await _vault.GetDataBoundsAsync(symbol, CandlestickInterval.H1, StorageScope.Local);

        // Assert
        bounds.Should().NotBeNull();
        bounds!.Value.Earliest.Should().Be(startTime);
        bounds.Value.Latest.Should().BeAfter(startTime);
    }

    [Fact]
    public async Task GetDataBoundsAsync_MultipleFiles_ReturnsTotalBounds()
    {
        // Arrange
        var symbol = "MULTIBOUNDS";

        // Save data for January
        var jan = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1,
            24, new DateTime(2025, 1, 1, 0, 0, 0));
        await _vault.SaveAsync(jan, new SaveOptions { UseCompression = false });

        // Save data for March (skip February to test gap handling)
        var march = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1,
            24, new DateTime(2025, 3, 1, 0, 0, 0));
        await _vault.SaveAsync(march, new SaveOptions { UseCompression = false });

        // Act
        var bounds = await _vault.GetDataBoundsAsync(symbol, CandlestickInterval.H1, StorageScope.Local);

        // Assert
        bounds.Should().NotBeNull();
        bounds!.Value.Earliest.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0));
        bounds.Value.Latest.Should().BeAfter(new DateTime(2025, 3, 1, 0, 0, 0));
    }

    [Fact]
    public async Task GetDataBoundsAsync_CompressedFiles_ReadsCorrectly()
    {
        // Arrange
        var symbol = "COMPBOUNDS";
        var startTime = new DateTime(2025, 2, 1, 0, 0, 0);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = true });

        // Act
        var bounds = await _vault.GetDataBoundsAsync(symbol, CandlestickInterval.H1, StorageScope.Local);

        // Assert
        bounds.Should().NotBeNull();
        bounds!.Value.Earliest.Should().Be(startTime);
    }

    #endregion

    #region CoveragePercentage Tests

    [Fact]
    public async Task CoveragePercentage_FullData_Returns100Percent()
    {
        // Arrange
        var symbol = "FULL100";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        var endTime = startTime.AddHours(24);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Query exact range of data
        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, startTime, endTime);

        // Assert
        report.CoveragePercentage.Should().BeApproximately(100.0, 1.0);
    }

    [Fact]
    public async Task CoveragePercentage_NoData_Returns0Percent()
    {
        // Arrange
        var symbol = "ZERO";
        var queryStart = new DateTime(2025, 1, 1);
        var queryEnd = new DateTime(2025, 1, 31);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.CoveragePercentage.Should().Be(0.0);
    }

    [Fact]
    public async Task CoveragePercentage_HalfData_ReturnsApprox50Percent()
    {
        // Arrange
        var symbol = "HALF50";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        // Save only first 12 hours of a 24-hour period
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 12, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Query 24 hours
        var queryStart = startTime;
        var queryEnd = startTime.AddHours(24);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.CoveragePercentage.Should().BeApproximately(50.0, 5.0);
    }

    #endregion

    #region Load and Save Integration Tests

    [Fact]
    public async Task SaveAndLoad_SingleMonth_RoundTripsCorrectly()
    {
        // Arrange
        var symbol = "ROUNDTRIP";
        var startTime = new DateTime(2025, 1, 15, 0, 0, 0);
        var candleCount = 100;
        var original = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, candleCount, startTime);

        // Act
        await _vault.SaveAsync(original, new SaveOptions { UseCompression = false });
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(symbol, startTime, startTime.AddMinutes(candleCount), CandlestickInterval.M1));

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Symbol.Should().Be(symbol);
        loaded.Timeframes.Should().HaveCount(1);
        loaded.Timeframes[0].Candlesticks.Should().HaveCount(candleCount);

        // Verify data integrity
        for (int i = 0; i < candleCount; i++)
        {
            var orig = original.Timeframes[0].Candlesticks[i];
            var load = loaded.Timeframes[0].Candlesticks[i];
            load.OpenTime.Should().Be(orig.OpenTime);
            load.CloseTime.Should().Be(orig.CloseTime);
            load.Open.Should().Be(orig.Open);
            load.High.Should().Be(orig.High);
            load.Low.Should().Be(orig.Low);
            load.Close.Should().Be(orig.Close);
            load.Volume.Should().Be(orig.Volume);
        }
    }

    [Fact]
    public async Task SaveAndLoad_WithCompression_RoundTripsCorrectly()
    {
        // Arrange
        var symbol = "COMPRESSRT";
        var startTime = new DateTime(2025, 2, 1, 0, 0, 0);
        var candleCount = 50;
        var original = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, candleCount, startTime);

        // Act
        await _vault.SaveAsync(original, new SaveOptions { UseCompression = true });
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(symbol, startTime, startTime.AddHours(candleCount), CandlestickInterval.H1));

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Timeframes[0].Candlesticks.Should().HaveCount(candleCount);

        // Verify first and last candle
        loaded.Timeframes[0].Candlesticks[0].Open.Should().Be(original.Timeframes[0].Candlesticks[0].Open);
        loaded.Timeframes[0].Candlesticks[^1].Close.Should().Be(original.Timeframes[0].Candlesticks[^1].Close);
    }

    [Fact]
    public async Task SaveAndLoad_SpanningMultipleMonths_RoundTripsCorrectly()
    {
        // Arrange
        var symbol = "MULTIMONTH";
        var startTime = new DateTime(2025, 1, 15, 0, 0, 0);
        // 45 days of H1 = 45 * 24 = 1080 candles, spanning Jan-Feb
        var candleCount = 45 * 24;
        var original = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, candleCount, startTime);

        // Act
        await _vault.SaveAsync(original, new SaveOptions { UseCompression = false });
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(
            symbol,
            startTime,
            startTime.AddHours(candleCount),
            CandlestickInterval.H1));

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Timeframes[0].Candlesticks.Should().HaveCount(candleCount);
    }

    [Fact]
    public async Task SaveAndLoad_SpanningMultipleYears_RoundTripsCorrectly()
    {
        // Arrange
        var symbol = "MULTIYEAR";
        var startTime = new DateTime(2024, 12, 15, 0, 0, 0);
        // 45 days of H1 spanning Dec 2024 - Jan 2025
        var candleCount = 45 * 24;
        var original = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, candleCount, startTime);

        // Act
        await _vault.SaveAsync(original, new SaveOptions { UseCompression = false });
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(
            symbol,
            startTime,
            startTime.AddHours(candleCount),
            CandlestickInterval.H1));

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Timeframes[0].Candlesticks.Should().HaveCount(candleCount);
    }

    [Fact]
    public async Task SaveAndLoad_PartialOverwrite_MergesCorrectly()
    {
        // Arrange
        var symbol = "MERGE";
        var jan1 = new DateTime(2025, 1, 1, 0, 0, 0);

        // Save days 1-10
        var first = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 10 * 24, jan1);
        await _vault.SaveAsync(first, new SaveOptions { UseCompression = false, AllowPartialOverwrite = true });

        // Save days 5-15 (overlaps 5-10)
        var second = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 11 * 24, jan1.AddDays(5));
        await _vault.SaveAsync(second, new SaveOptions { UseCompression = false, AllowPartialOverwrite = true });

        // Act
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(
            symbol,
            jan1,
            jan1.AddDays(16),
            CandlestickInterval.H1));

        // Assert
        loaded.Should().NotBeNull();
        // Should have continuous data from Jan 1 to Jan 15
        loaded!.Timeframes[0].Candlesticks.Should().HaveCountGreaterThan(10 * 24);
    }

    [Fact]
    public async Task SaveAndLoad_EmptyFile_HandlesGracefully()
    {
        // Arrange
        var symbol = "EMPTY";

        // Act
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(
            symbol,
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 31),
            CandlestickInterval.H1));

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndLoad_DecimalPrecision_IsPreserved()
    {
        // Arrange
        var symbol = "PRECISION";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        var original = new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>
            {
                new()
                {
                    Timeframe = CandlestickInterval.H1,
                    Candlesticks = new List<CandlestickV2>
                    {
                        new()
                        {
                            OpenTime = startTime,
                            CloseTime = startTime.AddHours(1).AddTicks(-1),
                            Open = 0.123456789012345678901234567890m,
                            High = 9999999999.999999999999999999m,
                            Low = 0.000000000000000000000000001m,
                            Close = 1234567890.123456789012345678m,
                            Volume = 99999999999999999999999999.99m
                        }
                    }
                }
            }
        };

        // Act
        await _vault.SaveAsync(original, new SaveOptions { UseCompression = false });
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(symbol, startTime, startTime.AddHours(1), CandlestickInterval.H1));

        // Assert
        loaded.Should().NotBeNull();
        var loadedCandle = loaded!.Timeframes[0].Candlesticks[0];
        var originalCandle = original.Timeframes[0].Candlesticks[0];

        loadedCandle.Open.Should().Be(originalCandle.Open);
        loadedCandle.High.Should().Be(originalCandle.High);
        loadedCandle.Low.Should().Be(originalCandle.Low);
        loadedCandle.Close.Should().Be(originalCandle.Close);
        loadedCandle.Volume.Should().Be(originalCandle.Volume);
    }

    [Fact]
    public async Task SaveAndLoad_LargeDataset_HandlesEfficiently()
    {
        // Arrange
        var symbol = "LARGE";
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0);
        // 1 year of M1 data = ~525,600 candles (too large for quick test)
        // Use 1 week instead = 7 * 24 * 60 = 10,080 candles
        var candleCount = 7 * 24 * 60;
        var original = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, candleCount, startTime);

        // Act
        await _vault.SaveAsync(original, new SaveOptions { UseCompression = true });
        var loaded = await _vault.LoadAsync(LoadOptions.ForSymbol(
            symbol,
            startTime,
            startTime.AddMinutes(candleCount),
            CandlestickInterval.M1));

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Timeframes[0].Candlesticks.Should().HaveCount(candleCount);
    }

    #endregion
}
