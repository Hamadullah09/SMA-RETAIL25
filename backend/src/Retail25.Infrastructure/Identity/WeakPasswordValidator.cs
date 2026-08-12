using Microsoft.AspNetCore.Identity;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Refuses passwords that are weak in the way that actually gets accounts taken: guessable, rather
/// than insufficiently punctuated.
/// <para>
/// Identity's built-in rules are composition rules — a digit, a capital, a symbol. They are what
/// produce <c>Password1!</c>, which satisfies every one of them and is among the first thousand
/// strings any attacker tries. NIST SP 800-63B stopped recommending composition rules for exactly
/// that reason and recommends this instead: length, and a check against the passwords people
/// actually choose.
/// </para>
/// <para>
/// The list here is short and deliberately so. It is not a breach corpus — a real deployment should
/// point at one — but it covers the shapes a shop actually produces: the product's own name, the
/// year, the town, keyboard walks, and the handful of strings that top every leaked-password
/// analysis. Catching those is most of the value; catching the long tail needs a file this
/// repository has no business carrying.
/// </para>
/// </summary>
public sealed class WeakPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public static readonly string TooCommonCode = "PasswordTooCommon";
    public static readonly string ContainsIdentityCode = "PasswordContainsIdentity";

    /// <summary>
    /// Compared after lower-casing and stripping the separators people use to dress a weak password
    /// up — <c>P@ssw0rd</c> and <c>p-a-s-s-w-o-r-d</c> are the same guess.
    /// </summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "password", "passw0rd", "password1", "password123", "letmein", "welcome", "welcome1",
        "qwerty", "qwertyuiop", "asdfgh", "zxcvbn", "1qaz2wsx", "qazwsx",
        "12345678", "123456789", "1234567890", "111111", "000000", "123123", "abc123",
        "iloveyou", "admin", "administrator", "root", "changeme", "secret", "login",
        "sunshine", "princess", "dragon", "monkey", "football", "baseball", "master",
        "trustno1", "starwars", "whatever", "superman", "batman",
        // The shapes a shop produces on its own.
        "retail", "retail25", "smaretail", "sma", "smatechno", "pointofsale", "possystem",
        "cashier", "shop", "store", "till", "counter", "karachi", "pakistan", "lahore",
    };

    /// <summary>
    /// Substitutions that fool a literal comparison and nobody else. Applied before the lookup so
    /// <c>P@$$w0rd!</c> is recognised as <c>password</c>.
    /// </summary>
    private static readonly Dictionary<char, char> Leetspeak = new()
    {
        ['@'] = 'a', ['4'] = 'a', ['8'] = 'b', ['('] = 'c', ['3'] = 'e', ['6'] = 'g',
        // '1' reads as either 'i' or 'l'. 'i' is chosen because it is the one that appears in the
        // passwords people actually pick — adm1n, l3tm31n — and trying both readings would double
        // the candidate set for a letter that is rarely the deciding character.
        ['1'] = 'i', ['!'] = 'i', ['0'] = 'o', ['5'] = 's', ['$'] = 's', ['7'] = 't',
    };

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>();
        var normalised = Normalise(password);

        // Both readings are checked, because substitution cuts both ways. Applied blindly it turns
        // "retail25" into "retail2s" and "cashier1" into "cashierl", so the very passwords a shop
        // invents stop matching — which is what the first version of this did, and what its own
        // tests caught. Digits are legitimate characters as often as they are disguised letters,
        // and there is no way to tell which from the string alone, so it asks both questions.
        // Trailing punctuation is trimmed before substitution, not after: the '!' on "P@$$w0rd!" is
        // decoration, and decoding it to an 'i' turns a match into "passwordi".
        var decoded = Substitute(password.ToLowerInvariant().TrimEnd('!', '?', '.', '*', '#', '@', '$'));
        var candidates = new[] { normalised, Normalise(decoded) };

        // Trailing digits are dropped as well, because "password2026" and "welcome123" are the same
        // guess with a year on the end.
        var stems = candidates
            .Select(c => c.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
            .Where(s => s.Length >= 3);

        if (candidates.Any(Common.Contains) || stems.Any(Common.Contains))
        {
            errors.Add(new IdentityError
            {
                Code = TooCommonCode,
                Description = "That password is one of the first an attacker would try. Choose something less predictable.",
            });
        }

        // A password built from the account it protects is public knowledge. Checked against the
        // parts rather than the whole so "ayesha.khan@sma.rms.com" does not merely fail on itself.
        foreach (var part in IdentityParts(user))
        {
            if (part.Length >= 4 && normalised.Contains(part, StringComparison.Ordinal))
            {
                errors.Add(new IdentityError
                {
                    Code = ContainsIdentityCode,
                    Description = "A password must not contain your name or email address.",
                });

                break;
            }
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    /// <summary>
    /// Lower-cased, with the dressing removed. Spaces, hyphens and dots are dropped so
    /// <c>p-a-s-s-w-o-r-d</c> reads as what it is; letters and digits are kept exactly as typed.
    /// </summary>
    private static string Normalise(string password)
    {
        var builder = new System.Text.StringBuilder(password.Length);

        foreach (var character in password.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The same string read as if every digit and symbol were standing in for a letter.
    /// <para>
    /// Runs on the raw password, before <see cref="Normalise"/> strips punctuation — the symbols
    /// <em>are</em> the substitution in <c>P@$$w0rd</c>, so dropping them first destroys the very
    /// thing being decoded and leaves "psswrd", which matches nothing.
    /// </para>
    /// <para>
    /// And it is a second reading rather than a replacement, because applied in place it breaks
    /// every password that ends in a year: "retail25" becomes "retail2s".
    /// </para>
    /// </summary>
    private static string Substitute(string lowercased)
    {
        var builder = new System.Text.StringBuilder(lowercased.Length);

        foreach (var character in lowercased)
        {
            builder.Append(Leetspeak.TryGetValue(character, out var plain) ? plain : character);
        }

        return builder.ToString();
    }

    private static IEnumerable<string> IdentityParts(ApplicationUser user)
    {
        var candidates = new[]
        {
            user.Email?.Split('@')[0],
            user.UserName?.Split('@')[0],
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            // The whole local part, which is what "do not use your username" means.
            var whole = Normalise(candidate);
            if (whole.Length >= 4)
            {
                yield return whole;
            }
        }

        // And the words of the person's name, which is the other half of the rule. Deliberately
        // only the display name: splitting an *address* into fragments treats every hyphenated
        // word in it as forbidden, and addresses are full of ordinary words. That version rejected
        // "Integration!…" for an account at integration-admin@… because the address contributed
        // "integration" — and since the seeded administrator is created through this validator, it
        // meant a fresh deployment could not create an administrator at all. CI caught it; the
        // local suite could not, because those tests need a database.
        foreach (var word in (user.DisplayName ?? string.Empty)
                     .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalised = Normalise(word);
            if (normalised.Length >= 4)
            {
                yield return normalised;
            }
        }
    }
}
