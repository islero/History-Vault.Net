using FluentAssertions;
using HistoryVault.Models;
using Xunit;

namespace HistoryVault.Tests.Models;

public class DataAvailabilityReportTests
{
    [Fact]
    public void CoveragePercentage_FullCoverage_ReturnsOne()
    {
        // Arrange
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = new[] { new DateRange(queryStart, queryEnd) },
            MissingRanges = Array.Empty<DateRange>(),
            TotalCandlesAvailable = 12,
            ExpectedCandlesCount = 12
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(1.0);
    }

    [Fact]
    public void CoveragePercentage_NoCoverage_ReturnsZero()
    {
        // Arrange
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = Array.Empty<DateRange>(),
            MissingRanges = new[] { new DateRange(queryStart, queryEnd) },
            TotalCandlesAvailable = 0,
            ExpectedCandlesCount = 12
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(0.0);
    }

    [Fact]
    public void CoveragePercentage_HalfCoverage_ReturnsPointFive()
    {
        // Arrange
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);
        var midPoint = new DateTime(2025, 1, 1, 6, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = new[] { new DateRange(queryStart, midPoint) },
            MissingRanges = new[] { new DateRange(midPoint, queryEnd) },
            TotalCandlesAvailable = 6,
            ExpectedCandlesCount = 12
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(0.5);
    }

    [Fact]
    public void CoveragePercentage_MultipleRanges_CalculatesCorrectTotal()
    {
        // Arrange
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);

        // Three 2-hour blocks = 6 hours of 12 hours total = 50%
        var range1 = new DateRange(new DateTime(2025, 1, 1, 0, 0, 0), new DateTime(2025, 1, 1, 2, 0, 0));
        var range2 = new DateRange(new DateTime(2025, 1, 1, 4, 0, 0), new DateTime(2025, 1, 1, 6, 0, 0));
        var range3 = new DateRange(new DateTime(2025, 1, 1, 8, 0, 0), new DateTime(2025, 1, 1, 10, 0, 0));

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = new[] { range1, range2, range3 },
            MissingRanges = Array.Empty<DateRange>(),
            TotalCandlesAvailable = 6,
            ExpectedCandlesCount = 12
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(0.5);
    }

    [Fact]
    public void CoveragePercentage_QueryStartEqualsQueryEnd_ReturnsZero()
    {
        // Arrange
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = timestamp,
            QueryEnd = timestamp,
            AvailableRanges = Array.Empty<DateRange>(),
            MissingRanges = Array.Empty<DateRange>(),
            TotalCandlesAvailable = 0,
            ExpectedCandlesCount = 0
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(0.0);
    }

    [Fact]
    public void CoveragePercentage_QueryStartAfterQueryEnd_ReturnsZero()
    {
        // Arrange
        var queryStart = new DateTime(2025, 1, 2, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 0, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = Array.Empty<DateRange>(),
            MissingRanges = Array.Empty<DateRange>(),
            TotalCandlesAvailable = 0,
            ExpectedCandlesCount = 0
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(0.0);
    }

    [Fact]
    public void CoveragePercentage_ExceedsFullRange_CapsAtOne()
    {
        // Arrange - Available range extends beyond query range
        var queryStart = new DateTime(2025, 1, 1, 6, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);

        // Available range is larger than query range
        var availableStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var availableEnd = new DateTime(2025, 1, 1, 18, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = new[] { new DateRange(availableStart, availableEnd) },
            MissingRanges = Array.Empty<DateRange>(),
            TotalCandlesAvailable = 18,
            ExpectedCandlesCount = 6
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(1.0); // Capped at 100%
    }

    [Fact]
    public void CoveragePercentage_QuarterCoverage_ReturnsPointTwoFive()
    {
        // Arrange
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);
        var quarterEnd = new DateTime(2025, 1, 1, 3, 0, 0);

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = new[] { new DateRange(queryStart, quarterEnd) },
            MissingRanges = new[] { new DateRange(quarterEnd, queryEnd) },
            TotalCandlesAvailable = 3,
            ExpectedCandlesCount = 12
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().Be(0.25);
    }

    [Fact]
    public void CoveragePercentage_SmallGaps_CalculatesAccurately()
    {
        // Arrange - 95% coverage (11.4 hours of 12 hours)
        var queryStart = new DateTime(2025, 1, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 1, 1, 12, 0, 0);

        // Missing 36 minutes (0.6 hours) = 95% coverage
        var range1 = new DateRange(new DateTime(2025, 1, 1, 0, 0, 0), new DateTime(2025, 1, 1, 6, 0, 0));
        var range2 = new DateRange(new DateTime(2025, 1, 1, 6, 36, 0), new DateTime(2025, 1, 1, 12, 0, 0));

        var report = new DataAvailabilityReport
        {
            Symbol = "TEST",
            Timeframe = CandlestickInterval.H1,
            QueryStart = queryStart,
            QueryEnd = queryEnd,
            AvailableRanges = new[] { range1, range2 },
            MissingRanges = Array.Empty<DateRange>(),
            TotalCandlesAvailable = 11,
            ExpectedCandlesCount = 12
        };

        // Act
        var coverage = report.CoveragePercentage;

        // Assert
        coverage.Should().BeApproximately(0.95, 0.0001);
    }
}
