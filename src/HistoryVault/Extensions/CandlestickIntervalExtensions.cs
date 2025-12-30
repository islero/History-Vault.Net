using HistoryVault.Models;

namespace HistoryVault.Extensions;

/// <summary>
/// Extension methods for <see cref="CandlestickInterval"/>.
/// </summary>
public static class CandlestickIntervalExtensions
{
    /// <summary>
    /// Gets the duration of the interval as a TimeSpan.
    /// </summary>
    /// <param name="interval">The candlestick interval.</param>
    /// <returns>The duration of one candlestick of this interval.</returns>
    /// <exception cref="ArgumentException">Thrown for Tick or Custom intervals which have no fixed duration.</exception>
    public static TimeSpan ToTimeSpan(this CandlestickInterval interval)
    {
        if (interval == CandlestickInterval.Tick)
        {
            throw new ArgumentException("Tick interval has no fixed duration.", nameof(interval));
        }

        if (interval == CandlestickInterval.Custom)
        {
            throw new ArgumentException("Custom interval has no fixed duration.", nameof(interval));
        }

        return TimeSpan.FromSeconds((int)interval);
    }

    /// <summary>
    /// Gets the number of seconds in the interval.
    /// </summary>
    /// <param name="interval">The candlestick interval.</param>
    /// <returns>The number of seconds.</returns>
    public static int ToSeconds(this CandlestickInterval interval) => (int)interval;

    /// <summary>
    /// Determines whether one interval can be aggregated into another.
    /// </summary>
    /// <param name="source">The source (smaller) interval.</param>
    /// <param name="target">The target (larger) interval.</param>
    /// <returns>True if source can be aggregated into target; otherwise, false.</returns>
    public static bool CanAggregateTo(this CandlestickInterval source, CandlestickInterval target)
    {
        if (source == CandlestickInterval.Tick || source == CandlestickInterval.Custom)
        {
            return false;
        }

        if (target == CandlestickInterval.Tick || target == CandlestickInterval.Custom)
        {
            return false;
        }

        int sourceSeconds = source.ToSeconds();
        int targetSeconds = target.ToSeconds();

        return sourceSeconds > 0 && targetSeconds > sourceSeconds && (targetSeconds % sourceSeconds) == 0;
    }

    /// <summary>
    /// Gets the number of source intervals that fit into one target interval.
    /// </summary>
    /// <param name="source">The source (smaller) interval.</param>
    /// <param name="target">The target (larger) interval.</param>
    /// <returns>The number of source intervals per target interval.</returns>
    /// <exception cref="InvalidOperationException">Thrown when aggregation is not possible.</exception>
    public static int GetAggregationFactor(this CandlestickInterval source, CandlestickInterval target)
    {
        if (!source.CanAggregateTo(target))
        {
            throw new InvalidOperationException(
                $"Cannot aggregate {source} to {target}. Target must be a larger interval that is evenly divisible by source.");
        }

        return target.ToSeconds() / source.ToSeconds();
    }

    /// <summary>
    /// Gets a human-readable name for the interval.
    /// </summary>
    /// <param name="interval">The candlestick interval.</param>
    /// <returns>A human-readable name.</returns>
    public static string ToDisplayName(this CandlestickInterval interval)
    {
        return interval switch
        {
            CandlestickInterval.Tick => "Tick",
            CandlestickInterval.Second => "1 Second",
            CandlestickInterval.M1 => "1 Minute",
            CandlestickInterval.M3 => "3 Minutes",
            CandlestickInterval.M5 => "5 Minutes",
            CandlestickInterval.M10 => "10 Minutes",
            CandlestickInterval.M15 => "15 Minutes",
            CandlestickInterval.M30 => "30 Minutes",
            CandlestickInterval.H1 => "1 Hour",
            CandlestickInterval.H2 => "2 Hours",
            CandlestickInterval.H4 => "4 Hours",
            CandlestickInterval.H6 => "6 Hours",
            CandlestickInterval.H8 => "8 Hours",
            CandlestickInterval.H12 => "12 Hours",
            CandlestickInterval.D1 => "1 Day",
            CandlestickInterval.D3 => "3 Days",
            CandlestickInterval.W1 => "1 Week",
            CandlestickInterval.MN1 => "1 Month",
            CandlestickInterval.Custom => "Custom",
            _ => interval.ToString()
        };
    }

    /// <summary>
    /// Gets a short code for the interval suitable for file/directory naming.
    /// </summary>
    /// <param name="interval">The candlestick interval.</param>
    /// <returns>A short code string.</returns>
    public static string ToShortCode(this CandlestickInterval interval)
    {
        return interval switch
        {
            CandlestickInterval.Tick => "tick",
            CandlestickInterval.Second => "1s",
            CandlestickInterval.M1 => "1m",
            CandlestickInterval.M3 => "3m",
            CandlestickInterval.M5 => "5m",
            CandlestickInterval.M10 => "10m",
            CandlestickInterval.M15 => "15m",
            CandlestickInterval.M30 => "30m",
            CandlestickInterval.H1 => "1h",
            CandlestickInterval.H2 => "2h",
            CandlestickInterval.H4 => "4h",
            CandlestickInterval.H6 => "6h",
            CandlestickInterval.H8 => "8h",
            CandlestickInterval.H12 => "12h",
            CandlestickInterval.D1 => "1d",
            CandlestickInterval.D3 => "3d",
            CandlestickInterval.W1 => "1w",
            CandlestickInterval.MN1 => "1M",
            CandlestickInterval.Custom => "custom",
            _ => $"{(int)interval}s"
        };
    }

    /// <summary>
    /// Parses a short code string to a CandlestickInterval.
    /// </summary>
    /// <param name="code">The short code string.</param>
    /// <returns>The corresponding CandlestickInterval.</returns>
    /// <exception cref="ArgumentException">Thrown when the code is not recognized.</exception>
    public static CandlestickInterval FromShortCode(string code)
    {
        // Handle month (1M) before lowercase conversion to distinguish from 1m (minute)
        if (code == "1M")
        {
            return CandlestickInterval.MN1;
        }

        return code.ToLowerInvariant() switch
        {
            "tick" => CandlestickInterval.Tick,
            "1s" => CandlestickInterval.Second,
            "1m" => CandlestickInterval.M1,
            "3m" => CandlestickInterval.M3,
            "5m" => CandlestickInterval.M5,
            "10m" => CandlestickInterval.M10,
            "15m" => CandlestickInterval.M15,
            "30m" => CandlestickInterval.M30,
            "1h" => CandlestickInterval.H1,
            "2h" => CandlestickInterval.H2,
            "4h" => CandlestickInterval.H4,
            "6h" => CandlestickInterval.H6,
            "8h" => CandlestickInterval.H8,
            "12h" => CandlestickInterval.H12,
            "1d" => CandlestickInterval.D1,
            "3d" => CandlestickInterval.D3,
            "1w" => CandlestickInterval.W1,
            "custom" => CandlestickInterval.Custom,
            _ => throw new ArgumentException($"Unrecognized interval code: {code}", nameof(code))
        };
    }

    /// <summary>
    /// Aligns a DateTime to the start of the interval period.
    /// </summary>
    /// <param name="interval">The candlestick interval.</param>
    /// <param name="dateTime">The DateTime to align.</param>
    /// <returns>The aligned DateTime.</returns>
    public static DateTime AlignToInterval(this CandlestickInterval interval, DateTime dateTime)
    {
        if (interval == CandlestickInterval.Tick || interval == CandlestickInterval.Custom)
        {
            return dateTime;
        }

        long ticks = dateTime.Ticks;
        long intervalTicks = interval.ToTimeSpan().Ticks;
        long alignedTicks = (ticks / intervalTicks) * intervalTicks;

        return new DateTime(alignedTicks, dateTime.Kind);
    }

    /// <summary>
    /// Calculates the expected number of candlesticks for a given date range.
    /// </summary>
    /// <param name="interval">The candlestick interval.</param>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <returns>The expected number of candlesticks.</returns>
    public static int GetExpectedCandleCount(this CandlestickInterval interval, DateTime start, DateTime end)
    {
        if (interval == CandlestickInterval.Tick || interval == CandlestickInterval.Custom)
        {
            return 0;
        }

        if (end <= start)
        {
            return 0;
        }

        var duration = end - start;
        var intervalDuration = interval.ToTimeSpan();

        return (int)Math.Ceiling(duration.TotalSeconds / intervalDuration.TotalSeconds);
    }

    /// <summary>
    /// Gets all standard intervals ordered from smallest to largest.
    /// </summary>
    /// <returns>An enumerable of all standard intervals in ascending order.</returns>
    public static IEnumerable<CandlestickInterval> GetAllStandardIntervals()
    {
        yield return CandlestickInterval.Second;
        yield return CandlestickInterval.M1;
        yield return CandlestickInterval.M3;
        yield return CandlestickInterval.M5;
        yield return CandlestickInterval.M10;
        yield return CandlestickInterval.M15;
        yield return CandlestickInterval.M30;
        yield return CandlestickInterval.H1;
        yield return CandlestickInterval.H2;
        yield return CandlestickInterval.H4;
        yield return CandlestickInterval.H6;
        yield return CandlestickInterval.H8;
        yield return CandlestickInterval.H12;
        yield return CandlestickInterval.D1;
        yield return CandlestickInterval.D3;
        yield return CandlestickInterval.W1;
        yield return CandlestickInterval.MN1;
    }
}
