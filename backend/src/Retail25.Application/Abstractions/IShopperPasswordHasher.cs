namespace Retail25.Application.Abstractions;

/// <summary>
/// Hashes and verifies the passwords members of the public choose for the phone app.
/// <para>
/// A separate contract from <see cref="IPinHasher"/> even though the implementation delegates
/// straight to it. The primitive is genuinely the same and duplicating an Argon2id configuration
/// would be a way to get two of them that quietly disagree — but a PIN and a customer password are
/// different secrets with different threat models and different rotation stories, and an interface
/// whose documentation says "staff PIN" is the wrong thing for a handler about a shopper to be
/// calling. The cost of keeping both names honest is this file.
/// </para>
/// </summary>
public interface IShopperPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}
