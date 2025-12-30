using HistoryVault.Abstractions;
using HistoryVault.Extensions;
using HistoryVault.Models;

namespace HistoryVault.Aggregation;

/// <summary>
/// Aggregates candlesticks from smaller timeframes to larger timeframes following OHLCV rules.
/// </summary>
public sealed class CandlestickAggregator : ICandlestickAggregator
{
    /// <inheritdoc />
    public IReadOnlyList<CandlestickV2> Aggregate(
        IReadOnlyList<CandlestickV2> candles,
        CandlestickInterval sourceTimeframe,
        CandlestickInterval targetTimeframe)
    {
        if (candles == null || candles.Count == 0)
        {
            return Array.Empty<CandlestickV2>();
        }

        if (!CanAggregate(sourceTimeframe, targetTimeframe))
        {
            throw new InvalidOperationException(
                $"Cannot aggregate from {sourceTimeframe} to {targetTimeframe}. " +
                "Target must be a larger interval that is evenly divisible by source.");
        }

        int aggregationFactor = sourceTimeframe.GetAggregationFactor(targetTimeframe);
        var targetDuration = targetTimeframe.ToTimeSpan();
        var result = new List<CandlestickV2>(candles.Count / aggregationFactor + 1);

        var currentGroup = new List<CandlestickV2>(aggregationFactor);
        DateTime? currentPeriodStart = null;

        foreach (var candle in candles)
        {
            var periodStart = targetTimeframe.AlignToInterval(candle.OpenTime);

            if (currentPeriodStart == null)
            {
                currentPeriodStart = periodStart;
            }
            else if (periodStart != currentPeriodStart)
            {
                // Aggregate the previous group
                if (currentGroup.Count > 0)
                {
                    result.Add(AggregateGroup(currentGroup, targetDuration));
                    currentGroup.Clear();
                }
                currentPeriodStart = periodStart;
            }

            currentGroup.Add(candle);
        }

        // Don't forget the last group
        if (currentGroup.Count > 0)
        {
            result.Add(AggregateGroup(currentGroup, targetDuration));
        }

        return result;
    }

    /// <inheritdoc />
    public CandlestickV2 AggregateToSingle(IReadOnlyList<CandlestickV2> candles)
    {
        if (candles == null || candles.Count == 0)
        {
            throw new ArgumentException("Cannot aggregate empty candle collection.", nameof(candles));
        }

        if (candles.Count == 1)
        {
            return candles[0].Clone();
        }

        decimal high = decimal.MinValue;
        decimal low = decimal.MaxValue;
        decimal volume = 0;

        foreach (var candle in candles)
        {
            if (candle.High > high)
            {
                high = candle.High;
            }
            if (candle.Low < low)
            {
                low = candle.Low;
            }
            volume += candle.Volume;
        }

        return new CandlestickV2
        {
            OpenTime = candles[0].OpenTime,
            Open = candles[0].Open,
            High = high,
            Low = low,
            Close = candles[^1].Close,
            CloseTime = candles[^1].CloseTime,
            Volume = volume
        };
    }

    /// <inheritdoc />
    public bool CanAggregate(CandlestickInterval sourceTimeframe, CandlestickInterval targetTimeframe)
    {
        return sourceTimeframe.CanAggregateTo(targetTimeframe);
    }

    /// <inheritdoc />
    public IEnumerable<CandlestickInterval> GetPossibleTargetTimeframes(CandlestickInterval sourceTimeframe)
    {
        if (sourceTimeframe == CandlestickInterval.Tick || sourceTimeframe == CandlestickInterval.Custom)
        {
            yield break;
        }

        foreach (var interval in CandlestickIntervalExtensions.GetAllStandardIntervals())
        {
            if (sourceTimeframe.CanAggregateTo(interval))
            {
                yield return interval;
            }
        }
    }

    /// <summary>
    /// Aggregates candlesticks from a source timeframe to multiple target timeframes efficiently.
    /// </summary>
    /// <param name="candles">The source candlesticks.</param>
    /// <param name="sourceTimeframe">The source timeframe.</param>
    /// <param name="targetTimeframes">The target timeframes to aggregate to.</param>
    /// <returns>A dictionary of timeframe to aggregated candlesticks.</returns>
    public Dictionary<CandlestickInterval, IReadOnlyList<CandlestickV2>> AggregateToMultiple(
        IReadOnlyList<CandlestickV2> candles,
        CandlestickInterval sourceTimeframe,
        IEnumerable<CandlestickInterval> targetTimeframes)
    {
        var result = new Dictionary<CandlestickInterval, IReadOnlyList<CandlestickV2>>();

        // Sort targets by size to potentially reuse intermediate aggregations
        var sortedTargets = targetTimeframes
            .Where(tf => CanAggregate(sourceTimeframe, tf))
            .OrderBy(tf => (int)tf)
            .ToList();

        var currentCandles = candles;
        var currentTimeframe = sourceTimeframe;

        foreach (var target in sortedTargets)
        {
            // Check if we can aggregate from current intermediate result
            if (currentTimeframe.CanAggregateTo(target))
            {
                var aggregated = Aggregate(currentCandles, currentTimeframe, target);
                result[target] = aggregated;

                // Use this as the new base for larger timeframes
                currentCandles = aggregated;
                currentTimeframe = target;
            }
            else
            {
                // Need to aggregate from source
                result[target] = Aggregate(candles, sourceTimeframe, target);
            }
        }

        return result;
    }

    /// <summary>
    /// Validates that a sequence of candlesticks is properly ordered and within the expected timeframe.
    /// </summary>
    /// <param name="candles">The candlesticks to validate.</param>
    /// <param name="expectedTimeframe">The expected timeframe.</param>
    /// <returns>True if valid; false if there are issues.</returns>
    public bool ValidateCandleSequence(IReadOnlyList<CandlestickV2> candles, CandlestickInterval expectedTimeframe)
    {
        if (candles == null || candles.Count == 0)
        {
            return true;
        }

        if (expectedTimeframe == CandlestickInterval.Tick || expectedTimeframe == CandlestickInterval.Custom)
        {
            // Just check ordering for tick/custom
            for (int i = 1; i < candles.Count; i++)
            {
                if (candles[i].OpenTime < candles[i - 1].OpenTime)
                {
                    return false;
                }
            }
            return true;
        }

        var expectedDuration = expectedTimeframe.ToTimeSpan();
        var tolerance = TimeSpan.FromSeconds(1); // Allow 1 second tolerance

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];

            // Check candle duration
            var actualDuration = candle.CloseTime - candle.OpenTime;
            if (Math.Abs((actualDuration - expectedDuration).TotalSeconds) > tolerance.TotalSeconds)
            {
                // This might be a partial candle at the end, which is acceptable
                if (i < candles.Count - 1)
                {
                    return false;
                }
            }

            // Check ordering
            if (i > 0 && candle.OpenTime < candles[i - 1].OpenTime)
            {
                return false;
            }
        }

        return true;
    }

    private static CandlestickV2 AggregateGroup(List<CandlestickV2> group, TimeSpan targetDuration)
    {
        decimal high = decimal.MinValue;
        decimal low = decimal.MaxValue;
        decimal volume = 0;

        foreach (var candle in group)
        {
            if (candle.High > high)
            {
                high = candle.High;
            }
            if (candle.Low < low)
            {
                low = candle.Low;
            }
            volume += candle.Volume;
        }

        var openTime = group[0].OpenTime;
        var closeTime = openTime.Add(targetDuration).AddTicks(-1);

        // Use actual close time from last candle if it's within the period
        if (group[^1].CloseTime > openTime && group[^1].CloseTime <= closeTime.AddSeconds(1))
        {
            closeTime = group[^1].CloseTime;
        }

        return new CandlestickV2
        {
            OpenTime = openTime,
            Open = group[0].Open,
            High = high,
            Low = low,
            Close = group[^1].Close,
            CloseTime = closeTime,
            Volume = volume
        };
    }
}
