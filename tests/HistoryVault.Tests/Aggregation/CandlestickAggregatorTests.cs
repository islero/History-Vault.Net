using FluentAssertions;
using HistoryVault.Aggregation;
using HistoryVault.Models;
using Xunit;

namespace HistoryVault.Tests.Aggregation;

public class CandlestickAggregatorTests
{
    private readonly CandlestickAggregator _aggregator = new();

    [Fact]
    public void Aggregate_M1_To_M5_ProducesCorrectOHLCV()
    {
        // Arrange
        var startTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var candles = TestHelpers.GenerateCandles(10, CandlestickInterval.M1, startTime);

        // Act
        var result = _aggregator.Aggregate(candles, CandlestickInterval.M1, CandlestickInterval.M5);

        // Assert
        result.Should().HaveCount(2);

        // First M5 candle (candles 0-4)
        var first5Candles = candles.Take(5).ToList();
        result[0].Open.Should().Be(first5Candles[0].Open);
        result[0].Close.Should().Be(first5Candles[^1].Close);
        result[0].High.Should().Be(first5Candles.Max(c => c.High));
        result[0].Low.Should().Be(first5Candles.Min(c => c.Low));
        result[0].Volume.Should().Be(first5Candles.Sum(c => c.Volume));
        result[0].OpenTime.Should().Be(first5Candles[0].OpenTime);

        // Second M5 candle (candles 5-9)
        var second5Candles = candles.Skip(5).Take(5).ToList();
        result[1].Open.Should().Be(second5Candles[0].Open);
        result[1].Close.Should().Be(second5Candles[^1].Close);
        result[1].High.Should().Be(second5Candles.Max(c => c.High));
        result[1].Low.Should().Be(second5Candles.Min(c => c.Low));
        result[1].Volume.Should().Be(second5Candles.Sum(c => c.Volume));
    }

    [Fact]
    public void Aggregate_M1_To_H1_HandlesPartialPeriods()
    {
        // Arrange - 75 M1 candles = 1 full hour + 15 minutes partial
        var startTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var candles = TestHelpers.GenerateCandles(75, CandlestickInterval.M1, startTime);

        // Act
        var result = _aggregator.Aggregate(candles, CandlestickInterval.M1, CandlestickInterval.H1);

        // Assert
        result.Should().HaveCount(2); // 1 complete + 1 partial hour

        // First complete hour
        var firstHourCandles = candles.Take(60).ToList();
        result[0].Volume.Should().Be(firstHourCandles.Sum(c => c.Volume));

        // Partial second hour (15 minutes)
        var partialHourCandles = candles.Skip(60).ToList();
        result[1].Volume.Should().Be(partialHourCandles.Sum(c => c.Volume));
    }

    [Theory]
    [InlineData(CandlestickInterval.M5, CandlestickInterval.M1)]
    [InlineData(CandlestickInterval.H1, CandlestickInterval.M30)]
    [InlineData(CandlestickInterval.D1, CandlestickInterval.H1)]
    public void Aggregate_ValidatesSourceSmallerThanTarget(
        CandlestickInterval source,
        CandlestickInterval target)
    {
        // Arrange
        var candles = TestHelpers.GenerateCandles(10, source, DateTime.UtcNow);

        // Act
        var action = () => _aggregator.Aggregate(candles, source, target);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot aggregate*");
    }

    [Fact]
    public void AggregateToSingle_CombinesMultipleCandlesCorrectly()
    {
        // Arrange
        var candles = new List<CandlestickV2>
        {
            new() { OpenTime = new DateTime(2025, 1, 1), Open = 100, High = 110, Low = 95, Close = 105, Volume = 1000 },
            new() { OpenTime = new DateTime(2025, 1, 2), Open = 105, High = 120, Low = 100, Close = 115, Volume = 1500 },
            new() { OpenTime = new DateTime(2025, 1, 3), Open = 115, High = 118, Low = 90, Close = 95, Volume = 2000, CloseTime = new DateTime(2025, 1, 3, 23, 59, 59) }
        };

        // Act
        var result = _aggregator.AggregateToSingle(candles);

        // Assert
        result.Open.Should().Be(100);
        result.High.Should().Be(120); // Max of all highs
        result.Low.Should().Be(90);   // Min of all lows
        result.Close.Should().Be(95);
        result.Volume.Should().Be(4500); // Sum of all volumes
        result.OpenTime.Should().Be(candles[0].OpenTime);
        result.CloseTime.Should().Be(candles[^1].CloseTime);
    }

    [Fact]
    public void AggregateToSingle_ThrowsForEmptyCollection()
    {
        // Arrange
        var candles = new List<CandlestickV2>();

        // Act
        var action = () => _aggregator.AggregateToSingle(candles);

        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanAggregate_ReturnsTrueForValidPairs()
    {
        _aggregator.CanAggregate(CandlestickInterval.M1, CandlestickInterval.M5).Should().BeTrue();
        _aggregator.CanAggregate(CandlestickInterval.M1, CandlestickInterval.H1).Should().BeTrue();
        _aggregator.CanAggregate(CandlestickInterval.M5, CandlestickInterval.M15).Should().BeTrue();
        _aggregator.CanAggregate(CandlestickInterval.H1, CandlestickInterval.H4).Should().BeTrue();
        _aggregator.CanAggregate(CandlestickInterval.H1, CandlestickInterval.D1).Should().BeTrue();
    }

    [Fact]
    public void CanAggregate_ReturnsFalseForInvalidPairs()
    {
        _aggregator.CanAggregate(CandlestickInterval.M5, CandlestickInterval.M1).Should().BeFalse();
        _aggregator.CanAggregate(CandlestickInterval.M1, CandlestickInterval.M1).Should().BeFalse();
        _aggregator.CanAggregate(CandlestickInterval.Tick, CandlestickInterval.M1).Should().BeFalse();
        _aggregator.CanAggregate(CandlestickInterval.M3, CandlestickInterval.M5).Should().BeFalse(); // 300 not divisible by 180
    }

    [Fact]
    public void GetPossibleTargetTimeframes_ReturnsValidTargets()
    {
        // Act
        var targets = _aggregator.GetPossibleTargetTimeframes(CandlestickInterval.M1).ToList();

        // Assert
        targets.Should().Contain(CandlestickInterval.M3); // 180 / 60 = 3
        targets.Should().Contain(CandlestickInterval.M5);
        targets.Should().Contain(CandlestickInterval.M15);
        targets.Should().Contain(CandlestickInterval.M30);
        targets.Should().Contain(CandlestickInterval.H1);
    }

    [Fact]
    public void Aggregate_PreservesDecimalPrecision()
    {
        // Arrange
        var candles = new List<CandlestickV2>
        {
            new() { OpenTime = new DateTime(2025, 1, 1, 10, 0, 0), Open = 100.123456789m, High = 110.987654321m, Low = 95.111111111m, Close = 105.222222222m, Volume = 1000.333333333m },
            new() { OpenTime = new DateTime(2025, 1, 1, 10, 1, 0), Open = 105.222222222m, High = 115.444444444m, Low = 100.555555555m, Close = 110.666666666m, Volume = 2000.777777777m }
        };

        // Act
        var result = _aggregator.AggregateToSingle(candles);

        // Assert
        result.Open.Should().Be(100.123456789m);
        result.High.Should().Be(115.444444444m);
        result.Low.Should().Be(95.111111111m);
        result.Close.Should().Be(110.666666666m);
        result.Volume.Should().Be(3001.111111110m);
    }
}
