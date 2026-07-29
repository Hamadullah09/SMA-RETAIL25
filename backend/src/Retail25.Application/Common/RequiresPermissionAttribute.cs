namespace Retail25.Application.Common;

/// <summary>
/// Declares the permission a request needs. Enforced by <c>AuthorizationBehavior</c> in the MediatR
/// pipeline, so the check happens once per request rather than being re-implemented at every
/// transport — an endpoint, a hub method and a background job all get the same answer.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public RequiresPermissionAttribute(string permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// Marks a request whose refusal should read as "a supervisor can approve this", not "you cannot do
/// this". The API turns it into <c>428 sale.requires_supervisor</c> so the till can raise a step-up
/// prompt instead of a dead end (doc 05 error taxonomy).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SupportsSupervisorApprovalAttribute : Attribute
{
}
