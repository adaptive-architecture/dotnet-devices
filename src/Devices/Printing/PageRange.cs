namespace AdaptArch.Devices.Printing;

/// <summary>
/// A range of pages to print, counted from 1 and inclusive at both ends.
/// </summary>
public readonly struct PageRange : IEquatable<PageRange>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PageRange"/> struct.
    /// </summary>
    /// <param name="lower">The first page. The count starts at 1.</param>
    /// <param name="upper">The last page. It must not be before <paramref name="lower"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the range is not a page range.</exception>
    public PageRange(int lower, int upper)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lower, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(upper, lower);
        Lower = lower;
        Upper = upper;
    }

    /// <summary>
    /// Gets the first page of the range.
    /// </summary>
    public int Lower { get; }

    /// <summary>
    /// Gets the last page of the range.
    /// </summary>
    public int Upper { get; }

    /// <inheritdoc />
    public bool Equals(PageRange other) => Lower == other.Lower && Upper == other.Upper;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PageRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Lower, Upper);

    /// <summary>
    /// Returns the range as <c>lower-upper</c>.
    /// </summary>
    /// <returns>The text form of the range.</returns>
    public override string ToString() => $"{Lower}-{Upper}";

    /// <summary>
    /// Selects the pages of a document the ranges name.
    /// </summary>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <param name="ranges">The ranges the caller asked for, or <c>null</c> for the whole document.</param>
    /// <returns>Zero-based page indexes in ascending order, with no duplicates. A range past the end contributes nothing.</returns>
    /// <remarks>
    /// An <see cref="IPrintPayloadConverter"/> uses this to honour
    /// <see cref="PrintConversionContext.PageRanges"/> the same way the library does.
    /// </remarks>
    public static IReadOnlyList<int> Select(int pageCount, IReadOnlyList<PageRange>? ranges)
    {
        if (pageCount <= 0)
        {
            return [];
        }

        if (ranges is null || ranges.Count == 0)
        {
            List<int> all = new(pageCount);
            for (var i = 0; i < pageCount; i++)
            {
                all.Add(i);
            }

            return all;
        }

        // A collection expression over a set emits a compiler wrapper type that the
        // trimmer cannot keep intact, with no analyzer warning. A plain list is safe.
        SortedSet<int> selected = [];
        foreach (var range in ranges)
        {
            var lower = Math.Max(range.Lower, 1);
            var upper = Math.Min(range.Upper, pageCount);
            for (var page = lower; page <= upper; page++)
            {
                selected.Add(page - 1);
            }
        }

        List<int> ordered = new(selected.Count);
        ordered.AddRange(selected);
        return ordered;
    }

    /// <summary>
    /// Compares two ranges for equality.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns><c>true</c> when the two ranges are equal.</returns>
    public static bool operator ==(PageRange left, PageRange right) => left.Equals(right);

    /// <summary>
    /// Compares two ranges for inequality.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns><c>true</c> when the two ranges are not equal.</returns>
    public static bool operator !=(PageRange left, PageRange right) => !left.Equals(right);
}
