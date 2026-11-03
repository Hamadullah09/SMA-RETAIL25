using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RetailPlus.Core.Rules;

/// <summary>
/// WhatsApp numbers are kept in international form, "+" and the country code first (+923001234567), so no country's
/// dialling rules are needed in the program: spaces, dashes, dots and brackets are dropped and a leading 00 becomes +.
/// </summary>
public static class WhatsAppNumbers
{
    /// <summary>The number in international form; null for an empty one. Throws for a number without a country code.</summary>
    public static string? Normalize(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var text = number.Trim();
        var digits = new StringBuilder();
        bool plus = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '+' && digits.Length == 0 && !plus) plus = true;
            else if (char.IsAsciiDigit(ch)) digits.Append(ch);
            else if (ch is not (' ' or '-' or '.' or '(' or ')')) throw new FormatException($"{number} is not a phone number.");
        }
        var s = digits.ToString();
        if (!plus && s.StartsWith("00", StringComparison.Ordinal)) { s = s[2..]; plus = true; }
        if (!plus || s.Length < 8 || s.Length > 15 || s[0] == '0')
            throw new FormatException($"Enter {number} in international form, with + and the country code (like +923001234567).");
        return "+" + s;
    }

    /// <summary>True when the number can be normalized (or is empty).</summary>
    public static bool IsValid(string? number)
    {
        try
        {
            Normalize(number);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The digits WhatsApp's API takes for the recipient (no +).</summary>
    public static string ForApi(string normalized) => normalized.TrimStart('+');
}

/// <summary>The values a template's placeholders are filled with for one client.</summary>
public sealed record TemplateValues(string? FirstName, string? LastName, string? Company, decimal Points, decimal Balance, string? Store, string? ClientNumber);

/// <summary>
/// Message templates as WhatsApp Business approves them: a name, a language and a body whose placeholders are numbered
/// parameters. Here the body is written with names ({FirstName}, {Points}, {Store}…); each placeholder becomes the next
/// parameter, in the order it first appears, so the body sent for approval reads {{1}}, {{2}}… in the same order.
/// </summary>
public static partial class WhatsAppTemplates
{
    /// <summary>The placeholders a body may use.</summary>
    public static readonly IReadOnlyList<string> Known = ["FirstName", "LastName", "Name", "Points", "Balance", "Store", "ClientNumber"];

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex("^[a-z0-9_]{1,512}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[a-z]{2,3}(_[A-Z]{2})?$")]
    private static partial Regex LanguagePattern();

    /// <summary>The body's placeholders, each once, in the order they first appear.</summary>
    public static IReadOnlyList<string> Placeholders(string body) =>
        [.. Placeholder().Matches(body).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>A problem with a template for the person who wrote it, or null.</summary>
    public static string? Problem(string name, string language, string body)
    {
        if (!NamePattern().IsMatch(name)) return "A template name has lower-case letters, digits and underscores only (as at WhatsApp).";
        if (!LanguagePattern().IsMatch(language)) return "The language is a code like en, en_US or ur.";
        if (string.IsNullOrWhiteSpace(body)) return "Write the message.";
        if (body.Length > 1024) return "A template's text is at most 1024 characters.";
        var unknown = Placeholders(body).Where(p => !Known.Contains(p)).ToList();
        if (unknown.Count > 0)
            return $"Unknown placeholder {string.Join(", ", unknown.Select(u => "{" + u + "}"))}. Use {string.Join(", ", Known.Select(k => "{" + k + "}"))}.";
        return null;
    }

    private static string Value(string placeholder, TemplateValues v, CultureInfo culture) => placeholder switch
    {
        "FirstName" => (v.FirstName ?? v.Company ?? "").Trim(),
        "LastName" => (v.LastName ?? "").Trim(),
        "Name" => !string.IsNullOrWhiteSpace(v.FirstName) || !string.IsNullOrWhiteSpace(v.LastName)
            ? $"{v.FirstName} {v.LastName}".Trim() : (v.Company ?? "").Trim(),
        "Points" => v.Points.ToString("0.##", culture),
        "Balance" => v.Balance.ToString("N2", culture),
        "Store" => (v.Store ?? "").Trim(),
        "ClientNumber" => v.ClientNumber ?? "",
        _ => "",
    };

    /// <summary>The template's parameters for one client, in order (WhatsApp refuses empty ones, so a blank becomes "-").</summary>
    public static IReadOnlyList<string> Parameters(string body, TemplateValues values, CultureInfo? culture = null) =>
        [.. Placeholders(body).Select(p => Value(p, values, culture ?? CultureInfo.InvariantCulture)).Select(s => s.Length == 0 ? "-" : s)];

    /// <summary>The text the client reads: the body with its placeholders filled in.</summary>
    public static string Render(string body, TemplateValues values, CultureInfo? culture = null) =>
        Placeholder().Replace(body, m => Known.Contains(m.Groups[1].Value) ? Value(m.Groups[1].Value, values, culture ?? CultureInfo.InvariantCulture) : m.Value);

    /// <summary>The body as it is submitted to WhatsApp for approval, with numbered parameters ({{1}}, {{2}}…).</summary>
    public static string Numbered(string body)
    {
        var order = Placeholders(body).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i + 1);
        return Placeholder().Replace(body, m => order.TryGetValue(m.Groups[1].Value, out var n) ? "{{" + n.ToString(CultureInfo.InvariantCulture) + "}}" : m.Value);
    }
}
