namespace RetailPlus.Core.Rules;

/// <summary>
/// The names a user signs in with, at a till and on the web: their user name or their e-mail address, with the same
/// password. Neither depends on case. An e-mail address is kept trimmed and in lower case, and no name a user signs in
/// with may be another user's (see SetupService.SaveUserAsync).
/// </summary>
public static class SignInNames
{
    /// <summary>The longest e-mail address kept.</summary>
    public const int EmailLength = 100;

    /// <summary>An e-mail address as it is kept: trimmed and in lower case; null for none.</summary>
    public static string? Email(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim().ToLowerInvariant();

    /// <summary>
    /// A simple check of an e-mail address: one @ with something before it, a dot after it with something on both sides,
    /// and no spaces. The web's user form checks it the same way.
    /// </summary>
    public static bool IsEmail(string text)
    {
        int at = text.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != text.LastIndexOf('@') || text.Any(char.IsWhiteSpace)) return false;
        var domain = text.AsSpan(at + 1);
        int dot = domain.Length > 1 ? domain[1..].IndexOf('.') + 1 : 0;
        return dot > 0 && dot < domain.Length - 1;
    }

    /// <summary>True when what was typed may be an e-mail address rather than a user name.</summary>
    public static bool MayBeEmail(string text) => text.Contains('@', StringComparison.Ordinal);
}
