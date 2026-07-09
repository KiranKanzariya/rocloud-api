using System.Reflection;
using MediatR;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;

namespace ROCloud.Application.Common.Behaviours;

/// <summary>
/// Enforces <see cref="RequirePermissionsAttribute"/> declared on a request, using the
/// current user's permission claims. Requests without the attribute pass through.
/// </summary>
public class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUserService _currentUser;

    public AuthorizationBehaviour(ICurrentUserService currentUser) => _currentUser = currentUser;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attribute = request.GetType().GetCustomAttribute<RequirePermissionsAttribute>();
        if (attribute is { Permissions.Length: > 0 })
        {
            if (!_currentUser.IsAuthenticated)
                throw new ForbiddenAccessException("Authentication is required.");

            if (!attribute.Permissions.All(_currentUser.Permissions.Contains))
                throw new ForbiddenAccessException();
        }

        return await next();
    }
}
