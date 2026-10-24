using System.Reflection;
using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Auth;
using Retail25.Application.Common;
using Retail25.Domain.Common;

namespace Retail25.Application.Behaviors;

/// <summary>
/// Thrown when the actor lacks a permission a request declares. The API maps it to 403 with the
/// machine-readable code, or to 428 when the action is one a supervisor could approve.
/// </summary>
public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string permission, bool supportsSupervisorApproval)
        : this(permission, supportsSupervisorApproval, action: null, reason: null)
    {
    }

    /// <param name="action">
    /// The request that was refused. Sent back with a step-up refusal so the till can raise the
    /// approval for exactly this command without carrying its own copy of every command's name.
    /// </param>
    /// <param name="reason">
    /// Why a grant the request carried could not be used — expired, already spent, raised by somebody
    /// else. Null when no grant was offered.
    /// </param>
    public PermissionDeniedException(string permission, bool supportsSupervisorApproval, string? action, Error? reason)
        : base(reason?.Message ?? $"The current user does not hold '{permission}'.")
    {
        Permission = permission;
        SupportsSupervisorApproval = supportsSupervisorApproval;
        Action = action;
        Reason = reason;
    }

    public PermissionDeniedException()
    {
        Permission = string.Empty;
    }

    public PermissionDeniedException(string message)
        : base(message) => Permission = string.Empty;

    public PermissionDeniedException(string message, Exception innerException)
        : base(message, innerException) => Permission = string.Empty;

    public string Permission { get; }

    public bool SupportsSupervisorApproval { get; }

    public string? Action { get; }

    public Error? Reason { get; }
}

/// <summary>
/// Enforces <see cref="RequiresPermissionAttribute"/> before validation runs, so a request the actor
/// may not make is refused without its contents being inspected at all.
/// <para>
/// It is also where a supervisor's grant is spent. It used to be left to the handler, which could
/// never happen: a cashier without the permission was refused here, on the first attempt and on the
/// retry carrying the approval alike, so the handler that would have accepted the grant never ran.
/// Spending it here, against the exact permission and command being refused, is the only place it
/// can be both reached and trusted.
/// </para>
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUser _currentUser;
    private readonly IStepUpGrants _grants;
    private readonly StepUpContext _stepUp;

    public AuthorizationBehavior(ICurrentUser currentUser, IStepUpGrants grants, StepUpContext stepUp)
    {
        _currentUser = currentUser;
        _grants = grants;
        _stepUp = stepUp;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var required = typeof(TRequest).GetCustomAttributes<RequiresPermissionAttribute>(inherit: false).ToList();

        if (required.Count == 0)
        {
            return await next();
        }

        var supportsStepUp = typeof(TRequest).GetCustomAttribute<SupportsSupervisorApprovalAttribute>() is not null;
        var action = typeof(TRequest).Name;

        foreach (var attribute in required)
        {
            if (_currentUser.HasPermission(attribute.Permission))
            {
                continue;
            }

            if (supportsStepUp && request is ISupervisorApprovable { ApprovalId: { } approvalId })
            {
                var granted = await _grants.SpendAsync(approvalId, action, attribute.Permission, cancellationToken);

                if (granted.IsSuccess)
                {
                    _stepUp.Record(granted.Value);
                    continue;
                }

                // Still a step-up refusal, so the till asks again rather than giving up: an expired
                // grant needs a fresh one, not a different person at the till.
                throw new PermissionDeniedException(attribute.Permission, supportsSupervisorApproval: true, action, granted.Error);
            }

            throw new PermissionDeniedException(attribute.Permission, supportsStepUp, action, reason: null);
        }

        return await next();
    }
}
