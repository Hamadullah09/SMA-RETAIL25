using System.Globalization;
using System.Text;

namespace Retail25.Application.Common;

/// <summary>
/// A page of rows plus the cursor that fetches the next one.
/// <para>
/// Cursor rather than offset (doc 05 §Conventions). A 50,000-row inventory browsed with
/// <c>OFFSET 40000</c> makes Postgres walk forty thousand rows to discard them, and a row inserted
/// mid-scroll silently shifts every later page — so a user paging through a catalogue sees an item
/// twice and never sees another. A keyset cursor has neither problem.
/// </para>
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);

/// <summary>
/// The position of the last row on a page: the value it sorted by, plus a unique tie-breaker.
/// <para>
/// Both parts are needed. Sorting by name alone is not a total order — two items called "Blue Shirt"
/// would page unpredictably, showing one twice and skipping the other. The tie-breaker is always a
/// column that is unique within the grid (a stock code, a customer number), never the primary key:
/// <c>uuid</c> ordering is meaningless to a user and does not match any index the grid sorts on.
/// </para>
/// </summary>
public static class Cursor
{
    /// <summary>
    /// Splits the two halves. A unit separator is used because it cannot occur inside a product
    /// name, a stock code or a company name — a comma or a colon can.
    /// </summary>
    private const char Separator = '\u001F';

    /// <summary>
    /// Base64 so a client cannot construct one by hand and come to depend on its shape: the cursor is
    /// an opaque continuation token, and changing the sort order later must not be a breaking change.
    /// </summary>
    public static string Encode(string sortKey, string tieBreak)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(sortKey, Separator.ToString(), tieBreak)));

    /// <summary>
    /// Decodes a cursor. A malformed one yields null rather than throwing — the sensible response to
    /// a mangled URL is the first page, not a 500.
    /// </summary>
    public static (string SortKey, string TieBreak)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(Separator, 2);
            return parts.Length == 2 ? (parts[0], parts[1]) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Clamps a caller's page size. Unbounded means one request can pull the whole catalogue.</summary>
    public static int PageSize(int requested, int max = 200) => Math.Clamp(requested <= 0 ? 50 : requested, 1, max);

    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a numeric half of a cursor. An unparseable one starts from the beginning.</summary>
    public static decimal? Decimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    public static long? Long(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
