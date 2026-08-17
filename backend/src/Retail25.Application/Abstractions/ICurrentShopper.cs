namespace Retail25.Application.Abstractions;

/// <summary>
/// The member of the public making this request, if it came from the phone app.
/// <para>
/// Deliberately a separate abstraction from <see cref="ICurrentUser"/> rather than another property
/// on it. The two are answers to different questions — "which employee is doing this, and what are
/// they allowed to do" versus "whose basket is this" — and a handler that wanted the second and was
/// handed the first is exactly the confusion this feature must not permit. Keeping them apart means
/// a shopper request cannot accidentally satisfy a staff check by having a non-null id in the field
/// the staff check happens to read.
/// </para>
/// <para>
/// There is no permission set here, and that absence is the design. A shopper is authorised by
/// <em>owning a live trolley session</em>, never by holding a grant.
/// </para>
/// </summary>
public interface ICurrentShopper
{
    long? ShopperId { get; }

    bool IsAuthenticated { get; }
}
