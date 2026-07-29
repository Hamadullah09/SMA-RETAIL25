namespace Retail25.Domain.ValueObjects;

/// <summary>
/// A postal address, stored as a JSON document on its owner. Field names follow the legacy import
/// layouts (user guide p.48 for clients, p.61 for suppliers) so migration is a direct mapping,
/// but no field is mandatory — international addresses that do not fit a US/Canada shape still work.
/// </summary>
public sealed record Address(
    string? Line1 = null,
    string? Line2 = null,
    string? City = null,
    string? StateOrProvince = null,
    string? PostalCode = null,
    string? Country = null)
{
    /// <summary>
    /// A blank address, for comparison and for callers that need one. Do <b>not</b> use it as a
    /// property initialiser: it is a single shared instance, and two entities that both default to it
    /// present the persistence layer with one owned object claimed by two owners.
    /// </summary>
    public static readonly Address Empty = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Line1) &&
        string.IsNullOrWhiteSpace(Line2) &&
        string.IsNullOrWhiteSpace(City) &&
        string.IsNullOrWhiteSpace(StateOrProvince) &&
        string.IsNullOrWhiteSpace(PostalCode) &&
        string.IsNullOrWhiteSpace(Country);

    /// <summary>
    /// Renders the address as lines for envelopes, shipping labels and invoices. Empty parts are
    /// dropped so a two-line address never prints a blank gap.
    /// </summary>
    public IReadOnlyList<string> ToLines()
    {
        var lines = new List<string>(4);

        AddIfPresent(lines, Line1);
        AddIfPresent(lines, Line2);

        var locality = string.Join(
            " ",
            new[] { City, StateOrProvince, PostalCode }.Where(part => !string.IsNullOrWhiteSpace(part)));
        AddIfPresent(lines, locality);

        AddIfPresent(lines, Country);

        return lines;

        static void AddIfPresent(List<string> target, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value.Trim());
            }
        }
    }

    public override string ToString() => string.Join(", ", ToLines());
}

/// <summary>
/// Telephone, fax, mobile and email for a person or company. Kept separate from
/// <see cref="Address"/> so clients and suppliers share one shape.
/// </summary>
public sealed record ContactDetails(
    string? Phone = null,
    string? Extension = null,
    string? Mobile = null,
    string? Fax = null,
    string? Email = null,
    string? Website = null)
{
    /// <summary>Shared blank instance. As with <see cref="Address.Empty"/>, never a property default.</summary>
    public static readonly ContactDetails Empty = new();
}
