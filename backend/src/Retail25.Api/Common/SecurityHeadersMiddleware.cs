namespace Retail25.Api.Common;

/// <summary>
/// The response headers from the Phase 1 hardening checklist (doc 07).
/// <para>
/// The API serves JSON and one login form, so the policy can be far stricter than a general web
/// app's: no scripts at all, no framing, no referrers. That matters because the login page is the
/// one place in the system where a password is typed, and it is served by the same process that
/// holds the token signing keys.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _contentSecurityPolicy;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        // Chrome applies `form-action` to the whole redirect chain of a form submission, and a
        // successful login POST ends in a redirect to the web app's callback. With 'self' alone the
        // browser blocks that redirect: the credentials are accepted, the session cookie is set,
        // and the operator is left staring at the login form as if nothing happened.
        var webOrigin = configuration["Auth:WebOrigin"] ?? "http://localhost:3000";

        _contentSecurityPolicy =
            "default-src 'none'; " +
            "style-src 'unsafe-inline'; " +   // the login page's own <style> block, nothing external

            // Exactly one script, pinned by hash: the login page's password reveal. This was
            // 'none', which is stronger in principle and unusable in practice — nothing but script
            // can change an input's type, and the CSS-only alternatives render the password in
            // plain text on Firefox. A hash keeps the property that mattered: an injected script
            // hashes differently and is still refused.
            $"script-src {LoginPageScript.CspHash}; " +
            "img-src 'self' data:; " +
            $"form-action 'self' {webOrigin.TrimEnd('/')}; " +
            "frame-ancestors 'none'; " +
            "base-uri 'none'";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        // The API never serves executable content or embeds anything. `default-src 'none'` means a
        // reflected-XSS payload has nothing it is permitted to load or run.
        headers["Content-Security-Policy"] = _contentSecurityPolicy;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        // Nothing here needs a camera, a microphone or a location, so all of it is refused rather
        // than left to a browser default that may change.
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";

        // Tokens and receipts must not sit in a shared proxy cache on a shop network.
        if (context.Request.Path.StartsWithSegments("/connect") || context.Request.Path.StartsWithSegments("/api"))
        {
            headers["Cache-Control"] = "no-store";
            headers["Pragma"] = "no-cache";
        }

        await _next(context);
    }
}
