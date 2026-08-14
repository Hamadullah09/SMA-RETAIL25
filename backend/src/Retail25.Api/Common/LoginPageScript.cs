using System.Security.Cryptography;
using System.Text;

namespace Retail25.Api.Common;

/// <summary>
/// The only script the API is allowed to run, and the hash that permits it.
/// <para>
/// The login page's content-security-policy was <c>default-src 'none'</c> with no
/// <c>script-src</c> at all, which is the strongest thing a page can say about itself: no script
/// runs, so an injected one cannot either. That is worth keeping on the one page in the system
/// where a password is typed, served by the process that holds the token signing keys.
/// </para>
/// <para>
/// A reveal control needs to change the input's <c>type</c>, and nothing but script can do that.
/// The CSS-only alternatives are worse than they look: <c>-webkit-text-security</c> requires the
/// field to be <c>type="text"</c>, which breaks password managers, and Firefox does not support
/// the property — so the password would render in plain text there. That is a security regression
/// dressed as a feature.
/// </para>
/// <para>
/// So the policy moves from "no scripts" to "this exact script", by hash. An injected script has a
/// different hash and is still refused, which is the property that actually mattered. The hash is
/// computed from the same constant that is written into the page, so the two cannot drift — a
/// hand-copied hash would silently stop matching the first time somebody edited the script, and
/// the reveal button would quietly stop working with only a console message to say why.
/// </para>
/// </summary>
public static class LoginPageScript
{
    /// <summary>
    /// Toggles the password field between hidden and visible.
    /// <para>
    /// Only the <c>type</c> attribute changes. Re-setting the value would lose the caret position
    /// and any in-progress selection, and would defeat a password manager that had just filled it.
    /// </para>
    /// <para>
    /// The two icons are both in the markup and one is hidden, so the script swaps a class rather
    /// than writing HTML. Building an element from a string here would be the one place in the
    /// system doing that, on the page that handles passwords, and the CSP exists to make exactly
    /// that impossible.
    /// </para>
    /// </summary>
    public const string Source =
        """
        (function () {
          var field = document.getElementById('password');
          var toggle = document.getElementById('pw-toggle');
          if (!field || !toggle) { return; }
          toggle.addEventListener('click', function () {
            var shown = field.getAttribute('type') === 'text';
            field.setAttribute('type', shown ? 'password' : 'text');
            toggle.setAttribute('aria-pressed', shown ? 'false' : 'true');
            toggle.setAttribute('aria-label', shown ? 'Show password' : 'Hide password');
            toggle.classList.toggle('revealed', !shown);
            field.focus();
          });
        })();
        """;

    /// <summary>
    /// The CSP source expression that permits exactly <see cref="Source"/>.
    /// <para>
    /// Computed once. The browser hashes the element's text content verbatim, so this has to be
    /// the same string that reaches the page — which is why both sides read this constant rather
    /// than each holding their own copy.
    /// </para>
    /// </summary>
    public static string CspHash { get; } =
        $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Source)))}'";
}
