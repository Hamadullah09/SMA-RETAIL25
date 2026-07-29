using System.Reflection;
using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;

namespace Retail25.Application.Behaviors;

/// <summary>
/// Thrown when the actor lacks a permission a request declares. The API maps it to 403 with the
/// machine-readable code, or to 428 when the action is one a supervisor could approve.
/// </summary>
public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string permission, bool supportsSupervisorApproval)
        : base($"The current user does not hold '{permission}'.")
    {
        Permission = permission;
        SupportsSupervisorApproval = supportsSupervisorApproval;
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
}

/// <summary>
/// Enforces <see cref="RequiresPermissionAttribute"/> before validation runs, so a request the actor
/// may not make is refused without its contents being inspected at all.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUser _currentUser;

    public AuthorizationBehavior(ICurrentUser currentUser) => _currentUser = currentUser;

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

        foreach (var attribute in required)
        {
            if (!_currentUser.HasPermission(attribute.Permission))
            {
                throw new PermissionDeniedException(attribute.Permission, supportsStepUp);
            }
        }

        return await next();
    }
}
