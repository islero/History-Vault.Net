using FluentAssertions;
using HistoryVault.Models;
using Xunit;

namespace HistoryVault.Tests.Models;

public class DateRangeTests
{
    #region IsAdjacentTo Tests

    [Fact]
    public void IsAdjacentTo_ExactAdjacency_ReturnsTrue()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 12, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 1, 12, 0, 0),
            new DateTime(2025, 1, 2, 0, 0, 0));

        // Act & Assert
        range1.IsAdjacentTo(range2).Should().BeTrue();
        range2.IsAdjacentTo(range1).Should().BeTrue();
    }

    [Fact]
    public void IsAdjacentTo_OneTickGap_ReturnsTrue()
    {
        // Arrange - simulates candle boundary: CloseTime = 23:59:59.9999999, next OpenTime = 00:00:00.0000000
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 23, 59, 59).AddTicks(9999999)); // 23:59:59.9999999
        var range2 = new DateRange(
            new DateTime(2025, 1, 2, 0, 0, 0), // 00:00:00.0000000
            new DateTime(2025, 1, 2, 23, 59, 59).AddTicks(9999999));

        // Act & Assert
        range1.IsAdjacentTo(range2).Should().BeTrue("1-tick gap should be treated as adjacent");
        range2.IsAdjacentTo(range1).Should().BeTrue("1-tick gap should be treated as adjacent (reversed)");
    }

    [Fact]
    public void IsAdjacentTo_TwoTickGap_ReturnsFalse()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 12, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 1, 12, 0, 0).AddTicks(2), // 2-tick gap
            new DateTime(2025, 1, 2, 0, 0, 0));

        // Act & Assert
        range1.IsAdjacentTo(range2).Should().BeFalse("2-tick gap should not be treated as adjacent");
        range2.IsAdjacentTo(range1).Should().BeFalse("2-tick gap should not be treated as adjacent (reversed)");
    }

    [Fact]
    public void IsAdjacentTo_LargeGap_ReturnsFalse()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 12, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 2, 0, 0, 0), // 12-hour gap
            new DateTime(2025, 1, 3, 0, 0, 0));

        // Act & Assert
        range1.IsAdjacentTo(range2).Should().BeFalse();
        range2.IsAdjacentTo(range1).Should().BeFalse();
    }

    [Fact]
    public void IsAdjacentTo_MonthBoundaryWithCandleStyle_ReturnsTrue()
    {
        // Arrange - simulates real candle scenario at month boundary
        // Last candle of June: CloseTime = June 30, 23:59:59.9999999
        // First candle of July: OpenTime = July 1, 00:00:00.0000000
        var juneRange = new DateRange(
            new DateTime(2025, 6, 30, 23, 0, 0),
            new DateTime(2025, 6, 30, 23, 59, 59).AddTicks(9999999));
        var julyRange = new DateRange(
            new DateTime(2025, 7, 1, 0, 0, 0),
            new DateTime(2025, 7, 1, 0, 59, 59).AddTicks(9999999));

        // Act & Assert
        juneRange.IsAdjacentTo(julyRange).Should().BeTrue("month boundary with candle timestamps should be adjacent");
    }

    [Fact]
    public void IsAdjacentTo_YearBoundaryWithCandleStyle_ReturnsTrue()
    {
        // Arrange - simulates year boundary
        // Last candle of Dec 2025: CloseTime = Dec 31, 23:59:59.9999999
        // First candle of Jan 2026: OpenTime = Jan 1, 00:00:00.0000000
        var decRange = new DateRange(
            new DateTime(2025, 12, 31, 23, 0, 0),
            new DateTime(2025, 12, 31, 23, 59, 59).AddTicks(9999999));
        var janRange = new DateRange(
            new DateTime(2026, 1, 1, 0, 0, 0),
            new DateTime(2026, 1, 1, 0, 59, 59).AddTicks(9999999));

        // Act & Assert
        decRange.IsAdjacentTo(janRange).Should().BeTrue("year boundary with candle timestamps should be adjacent");
    }

    [Fact]
    public void IsAdjacentTo_OverlappingRanges_ReturnsFalse()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 14, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 1, 12, 0, 0),
            new DateTime(2025, 1, 2, 0, 0, 0));

        // Act & Assert
        // Overlapping ranges are not adjacent (they overlap, which is different)
        range1.IsAdjacentTo(range2).Should().BeFalse();
    }

    #endregion

    #region Merge Tests with 1-Tick Gap

    [Fact]
    public void Merge_WithOneTickGap_MergesSuccessfully()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 23, 59, 59).AddTicks(9999999));
        var range2 = new DateRange(
            new DateTime(2025, 1, 2, 0, 0, 0),
            new DateTime(2025, 1, 2, 23, 59, 59).AddTicks(9999999));

        // Act
        var merged = range1.Merge(range2);

        // Assert
        merged.Start.Should().Be(range1.Start);
        merged.End.Should().Be(range2.End);
    }

    [Fact]
    public void Merge_WithTwoTickGap_ThrowsException()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 1, 12, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 1, 12, 0, 0).AddTicks(2),
            new DateTime(2025, 1, 2, 0, 0, 0));

        // Act & Assert
        var act = () => range1.Merge(range2);
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Contains_DateWithinRange_ReturnsTrue()
    {
        // Arrange
        var range = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 31, 23, 59, 59));
        var testDate = new DateTime(2025, 1, 15, 12, 0, 0);

        // Act & Assert
        range.Contains(testDate).Should().BeTrue();
    }

    [Fact]
    public void Contains_DateAtBoundary_ReturnsTrue()
    {
        // Arrange
        var range = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 31, 23, 59, 59));

        // Act & Assert
        range.Contains(range.Start).Should().BeTrue();
        range.Contains(range.End).Should().BeTrue();
    }

    [Fact]
    public void Contains_DateOutsideRange_ReturnsFalse()
    {
        // Arrange
        var range = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 31, 23, 59, 59));
        var testDate = new DateTime(2025, 2, 1, 0, 0, 0);

        // Act & Assert
        range.Contains(testDate).Should().BeFalse();
    }

    #endregion

    #region Overlaps Tests

    [Fact]
    public void Overlaps_PartialOverlap_ReturnsTrue()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 15, 0, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 10, 0, 0, 0),
            new DateTime(2025, 1, 20, 0, 0, 0));

        // Act & Assert
        range1.Overlaps(range2).Should().BeTrue();
        range2.Overlaps(range1).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_NoOverlap_ReturnsFalse()
    {
        // Arrange
        var range1 = new DateRange(
            new DateTime(2025, 1, 1, 0, 0, 0),
            new DateTime(2025, 1, 10, 0, 0, 0));
        var range2 = new DateRange(
            new DateTime(2025, 1, 15, 0, 0, 0),
            new DateTime(2025, 1, 20, 0, 0, 0));

        // Act & Assert
        range1.Overlaps(range2).Should().BeFalse();
        range2.Overlaps(range1).Should().BeFalse();
    }

    #endregion
}
