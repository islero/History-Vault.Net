namespace HistoryVault.Models;

/// <summary>
/// Represents a date/time range with start and end bounds.
/// </summary>
/// <param name="Start">The start of the range (inclusive).</param>
/// <param name="End">The end of the range (inclusive).</param>
public readonly record struct DateRange(DateTime Start, DateTime End)
{
    /// <summary>
    /// Gets the duration of the date range.
    /// </summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Determines whether this range contains the specified date.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <returns>True if the date is within the range; otherwise, false.</returns>
    public bool Contains(DateTime date) => date >= Start && date <= End;

    /// <summary>
    /// Determines whether this range overlaps with another range.
    /// </summary>
    /// <param name="other">The other range to check.</param>
    /// <returns>True if the ranges overlap; otherwise, false.</returns>
    public bool Overlaps(DateRange other) => Start <= other.End && End >= other.Start;

    /// <summary>
    /// Gets the intersection of this range with another range.
    /// </summary>
    /// <param name="other">The other range to intersect with.</param>
    /// <returns>The intersection range, or null if there is no overlap.</returns>
    public DateRange? Intersect(DateRange other)
    {
        if (!Overlaps(other))
        {
            return null;
        }

        return new DateRange(
            Start > other.Start ? Start : other.Start,
            End < other.End ? End : other.End
        );
    }

    /// <summary>
    /// Merges this range with an adjacent or overlapping range.
    /// </summary>
    /// <param name="other">The other range to merge with.</param>
    /// <returns>The merged range.</returns>
    /// <exception cref="InvalidOperationException">Thrown when ranges cannot be merged (not adjacent or overlapping).</exception>
    public DateRange Merge(DateRange other)
    {
        if (!Overlaps(other) && !IsAdjacentTo(other))
        {
            throw new InvalidOperationException("Cannot merge non-overlapping and non-adjacent ranges.");
        }

        return new DateRange(
            Start < other.Start ? Start : other.Start,
            End > other.End ? End : other.End
        );
    }

    /// <summary>
    /// Determines whether this range is adjacent to another range.
    /// Ranges are considered adjacent if the gap between them is at most 1 tick,
    /// which handles the standard candle boundary where CloseTime = OpenTime + Duration - 1 tick.
    /// </summary>
    /// <param name="other">The other range to check.</param>
    /// <returns>True if the ranges are adjacent; otherwise, false.</returns>
    public bool IsAdjacentTo(DateRange other)
    {
        // Allow 1-tick tolerance for candle boundaries
        long gapEndToStart = (other.Start - End).Ticks;
        long gapStartToEnd = (Start - other.End).Ticks;

        return (gapEndToStart >= 0 && gapEndToStart <= 1)
            || (gapStartToEnd >= 0 && gapStartToEnd <= 1);
    }
}
