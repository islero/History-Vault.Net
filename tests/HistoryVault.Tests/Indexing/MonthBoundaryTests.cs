using FluentAssertions;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Indexing;

/// <summary>
/// Tests for month boundary gap detection to verify that consecutive months
/// are properly merged without false gaps due to 1-tick precision mismatch.
/// </summary>
public class MonthBoundaryTests : IDisposable
{
    private readonly string _tempPath;
    private readonly HistoryVaultOptions _options;
    private readonly HistoryVaultStorage _vault;

    public MonthBoundaryTests()
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
    public async Task CheckAvailabilityAsync_ConsecutiveMonths_NoFalseGaps()
    {
        // Arrange - save data for June and July 2025
        var symbol = "MONTHBOUNDARY";

        // Generate June data (30 days * 24 hours = 720 H1 candles)
        var juneStart = new DateTime(2025, 6, 1, 0, 0, 0);
        var juneData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 30 * 24, juneStart);
        await _vault.SaveAsync(juneData, new SaveOptions { UseCompression = false });

        // Generate July data (31 days * 24 hours = 744 H1 candles)
        var julyStart = new DateTime(2025, 7, 1, 0, 0, 0);
        var julyData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 31 * 24, julyStart);
        await _vault.SaveAsync(julyData, new SaveOptions { UseCompression = false });

        // Query spanning both months
        var queryStart = juneStart;
        var queryEnd = new DateTime(2025, 7, 31, 23, 59, 59);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();

        // The key assertion: no gaps should be reported between June and July
        // Previously, a 1-tick gap at the month boundary caused false gaps
        report.MissingRanges.Should().NotContain(r =>
            r.Start.Month == 6 && r.End.Month == 7,
            "There should be no gap spanning the June-July boundary");

        // Ranges should merge into a single continuous range (or at most 2 if there's end gap)
        report.AvailableRanges.Should().HaveCountLessThanOrEqualTo(1,
            "Consecutive months should merge into a single available range");
    }

    [Fact]
    public async Task CheckAvailabilityAsync_YearBoundary_NoFalseGaps()
    {
        // Arrange - save data for December 2025 and January 2026
        var symbol = "YEARBOUNDARY";

        // Generate December data
        var decStart = new DateTime(2025, 12, 1, 0, 0, 0);
        var decData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 31 * 24, decStart);
        await _vault.SaveAsync(decData, new SaveOptions { UseCompression = false });

        // Generate January data
        var janStart = new DateTime(2026, 1, 1, 0, 0, 0);
        var janData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 31 * 24, janStart);
        await _vault.SaveAsync(janData, new SaveOptions { UseCompression = false });

        // Query spanning year boundary
        var queryStart = decStart;
        var queryEnd = new DateTime(2026, 1, 31, 23, 59, 59);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();

        // No gaps should be reported at the year boundary
        report.MissingRanges.Should().NotContain(r =>
            r.Start.Year == 2025 && r.End.Year == 2026,
            "There should be no gap spanning the Dec 2025 - Jan 2026 boundary");

        report.AvailableRanges.Should().HaveCountLessThanOrEqualTo(1,
            "Year boundary should not cause range fragmentation");
    }

    [Fact]
    public async Task CheckAvailabilityAsync_SixConsecutiveMonths_MergesIntoSingleRange()
    {
        // Arrange - save data for 6 consecutive months
        var symbol = "SIXMONTHS";
        var startMonth = new DateTime(2025, 1, 1, 0, 0, 0);

        int[] daysInMonth = { 31, 28, 31, 30, 31, 30 }; // Jan-Jun 2025

        for (int i = 0; i < 6; i++)
        {
            var monthStart = startMonth.AddMonths(i);
            var candleCount = daysInMonth[i] * 24;
            var monthData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, candleCount, monthStart);
            await _vault.SaveAsync(monthData, new SaveOptions { UseCompression = false });
        }

        // Query all 6 months
        var queryStart = startMonth;
        var queryEnd = new DateTime(2025, 6, 30, 23, 59, 59);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();

        // All 6 months should merge into a single available range
        report.AvailableRanges.Should().HaveCount(1,
            "6 consecutive months should merge into exactly 1 available range");

        // The single range should span from Jan 1 to end of June data
        report.AvailableRanges[0].Start.Should().Be(queryStart);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_RealGapBetweenMonths_DetectsGap()
    {
        // Arrange - save data for January and March (skip February = real gap)
        var symbol = "REALGAP";

        var janStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var janData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 31 * 24, janStart);
        await _vault.SaveAsync(janData, new SaveOptions { UseCompression = false });

        var marStart = new DateTime(2025, 3, 1, 0, 0, 0);
        var marData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 31 * 24, marStart);
        await _vault.SaveAsync(marData, new SaveOptions { UseCompression = false });

        // Query Jan-Mar
        var queryStart = janStart;
        var queryEnd = new DateTime(2025, 3, 31, 23, 59, 59);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();
        report.HasFullCoverage.Should().BeFalse();

        // Should detect the real gap in February
        report.MissingRanges.Should().NotBeEmpty("February should be detected as missing");
        report.MissingRanges.Should().Contain(r => r.Start.Month == 2 || r.End.Month == 2,
            "The missing range should include February");
    }

    [Fact]
    public async Task CheckAvailabilityAsync_M1Timeframe_MonthBoundary_NoFalseGaps()
    {
        // Arrange - test with M1 (more sensitive to tick precision)
        var symbol = "M1BOUNDARY";

        // Generate last day of June M1 data (24 * 60 = 1440 candles)
        var juneLastDay = new DateTime(2025, 6, 30, 0, 0, 0);
        var juneData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 24 * 60, juneLastDay);
        await _vault.SaveAsync(juneData, new SaveOptions { UseCompression = false });

        // Generate first day of July M1 data
        var julyFirstDay = new DateTime(2025, 7, 1, 0, 0, 0);
        var julyData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 24 * 60, julyFirstDay);
        await _vault.SaveAsync(julyData, new SaveOptions { UseCompression = false });

        // Query spanning the boundary
        var queryStart = juneLastDay;
        var queryEnd = new DateTime(2025, 7, 1, 23, 59, 59);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.M1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();
        report.HasAnyData.Should().BeTrue();

        // M1 data should also merge correctly across month boundary
        report.AvailableRanges.Should().HaveCountLessThanOrEqualTo(1,
            "M1 data should merge across month boundary without false gaps");
    }

    [Fact]
    public async Task CheckAvailabilityAsync_UserReportedScenario_JuneJulyBoundary()
    {
        // Arrange - reproduces the user-reported issue
        // Gap: 6/30/2025 11:58:00 PM - 6/30/2025 11:59:59 PM
        var symbol = "USERISSUE";

        // Generate data that ends at 11:59 PM on June 30
        var juneStart = new DateTime(2025, 6, 30, 0, 0, 0);
        var juneData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 24 * 60, juneStart);
        await _vault.SaveAsync(juneData, new SaveOptions { UseCompression = false });

        // Generate July data starting at midnight
        var julyStart = new DateTime(2025, 7, 1, 0, 0, 0);
        var julyData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 24 * 60, julyStart);
        await _vault.SaveAsync(julyData, new SaveOptions { UseCompression = false });

        // Query the exact boundary
        var queryStart = new DateTime(2025, 6, 30, 23, 0, 0);
        var queryEnd = new DateTime(2025, 7, 1, 1, 0, 0);

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.M1, queryStart, queryEnd);

        // Assert
        report.Should().NotBeNull();

        // The fix should prevent false gaps at the boundary
        // Any reported gap at 11:58-11:59 PM on June 30 would be a bug
        var falseGap = report.MissingRanges.FirstOrDefault(r =>
            r.Start >= new DateTime(2025, 6, 30, 23, 58, 0) &&
            r.End <= new DateTime(2025, 6, 30, 23, 59, 59));

        falseGap.Should().Be(default(DateRange),
            "No false gap should be reported at the end of June 30th");
    }
}
