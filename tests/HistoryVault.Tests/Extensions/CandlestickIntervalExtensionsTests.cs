using FluentAssertions;
using HistoryVault.Extensions;
using HistoryVault.Models;
using Xunit;

namespace HistoryVault.Tests.Extensions;

public class CandlestickIntervalExtensionsTests
{
    [Theory]
    [InlineData(CandlestickInterval.M1, 60)]
    [InlineData(CandlestickInterval.M5, 300)]
    [InlineData(CandlestickInterval.H1, 3600)]
    [InlineData(CandlestickInterval.D1, 86400)]
    public void ToSeconds_ReturnsCorrectValue(CandlestickInterval interval, int expected)
    {
        interval.ToSeconds().Should().Be(expected);
    }

    [Theory]
    [InlineData(CandlestickInterval.M1, 60)]
    [InlineData(CandlestickInterval.H1, 3600)]
    [InlineData(CandlestickInterval.D1, 86400)]
    public void ToTimeSpan_ReturnsCorrectDuration(CandlestickInterval interval, int expectedSeconds)
    {
        interval.ToTimeSpan().Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ToTimeSpan_Tick_ThrowsException()
    {
        var action = () => CandlestickInterval.Tick.ToTimeSpan();
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(CandlestickInterval.M1, CandlestickInterval.M5, true)]
    [InlineData(CandlestickInterval.M1, CandlestickInterval.H1, true)]
    [InlineData(CandlestickInterval.M1, CandlestickInterval.M3, true)]   // 180 / 60 = 3
    [InlineData(CandlestickInterval.M5, CandlestickInterval.M15, true)]
    [InlineData(CandlestickInterval.H1, CandlestickInterval.H4, true)]
    [InlineData(CandlestickInterval.M5, CandlestickInterval.M1, false)]  // Target smaller
    [InlineData(CandlestickInterval.M3, CandlestickInterval.M5, false)]  // 300 not divisible by 180
    [InlineData(CandlestickInterval.Tick, CandlestickInterval.M1, false)] // Tick source
    public void CanAggregateTo_ReturnsCorrectResult(
        CandlestickInterval source,
        CandlestickInterval target,
        bool expected)
    {
        source.CanAggregateTo(target).Should().Be(expected);
    }

    [Theory]
    [InlineData(CandlestickInterval.M1, CandlestickInterval.M5, 5)]
    [InlineData(CandlestickInterval.M1, CandlestickInterval.H1, 60)]
    [InlineData(CandlestickInterval.H1, CandlestickInterval.H4, 4)]
    [InlineData(CandlestickInterval.H1, CandlestickInterval.D1, 24)]
    public void GetAggregationFactor_ReturnsCorrectFactor(
        CandlestickInterval source,
        CandlestickInterval target,
        int expected)
    {
        source.GetAggregationFactor(target).Should().Be(expected);
    }

    [Fact]
    public void GetAggregationFactor_InvalidPair_ThrowsException()
    {
        var action = () => CandlestickInterval.H1.GetAggregationFactor(CandlestickInterval.M1);
        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(CandlestickInterval.M1, "1m")]
    [InlineData(CandlestickInterval.M5, "5m")]
    [InlineData(CandlestickInterval.H1, "1h")]
    [InlineData(CandlestickInterval.H4, "4h")]
    [InlineData(CandlestickInterval.D1, "1d")]
    [InlineData(CandlestickInterval.W1, "1w")]
    [InlineData(CandlestickInterval.MN1, "1M")]
    public void ToShortCode_ReturnsCorrectCode(CandlestickInterval interval, string expected)
    {
        interval.ToShortCode().Should().Be(expected);
    }

    [Theory]
    [InlineData("1m", CandlestickInterval.M1)]
    [InlineData("5m", CandlestickInterval.M5)]
    [InlineData("1h", CandlestickInterval.H1)]
    [InlineData("4h", CandlestickInterval.H4)]
    [InlineData("1d", CandlestickInterval.D1)]
    [InlineData("1w", CandlestickInterval.W1)]
    [InlineData("1M", CandlestickInterval.MN1)]
    public void FromShortCode_ReturnsCorrectInterval(string code, CandlestickInterval expected)
    {
        CandlestickIntervalExtensions.FromShortCode(code).Should().Be(expected);
    }

    [Fact]
    public void FromShortCode_InvalidCode_ThrowsException()
    {
        var action = () => CandlestickIntervalExtensions.FromShortCode("invalid");
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(CandlestickInterval.M1, "2025-01-15 10:37:45", "2025-01-15 10:37:00")]
    [InlineData(CandlestickInterval.M5, "2025-01-15 10:37:45", "2025-01-15 10:35:00")]
    [InlineData(CandlestickInterval.H1, "2025-01-15 10:37:45", "2025-01-15 10:00:00")]
    [InlineData(CandlestickInterval.D1, "2025-01-15 10:37:45", "2025-01-15 00:00:00")]
    public void AlignToInterval_AlignsCorrectly(
        CandlestickInterval interval,
        string inputStr,
        string expectedStr)
    {
        var input = DateTime.Parse(inputStr);
        var expected = DateTime.Parse(expectedStr);

        interval.AlignToInterval(input).Should().Be(expected);
    }

    [Fact]
    public void GetExpectedCandleCount_ReturnsCorrectCount()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0);
        var end = new DateTime(2025, 1, 1, 6, 0, 0); // 6 hours

        CandlestickInterval.H1.GetExpectedCandleCount(start, end).Should().Be(6);
        CandlestickInterval.M1.GetExpectedCandleCount(start, end).Should().Be(360); // 6 * 60
    }

    [Fact]
    public void GetAllStandardIntervals_ReturnsAllInOrder()
    {
        var intervals = CandlestickIntervalExtensions.GetAllStandardIntervals().ToList();

        intervals.Should().NotBeEmpty();
        intervals[0].Should().Be(CandlestickInterval.Second);
        intervals[^1].Should().Be(CandlestickInterval.MN1);

        // Should be in ascending order
        for (int i = 1; i < intervals.Count; i++)
        {
            ((int)intervals[i]).Should().BeGreaterThan((int)intervals[i - 1]);
        }
    }

    [Theory]
    [InlineData(CandlestickInterval.M1, "1 Minute")]
    [InlineData(CandlestickInterval.H1, "1 Hour")]
    [InlineData(CandlestickInterval.D1, "1 Day")]
    public void ToDisplayName_ReturnsReadableName(CandlestickInterval interval, string expected)
    {
        interval.ToDisplayName().Should().Be(expected);
    }
}
