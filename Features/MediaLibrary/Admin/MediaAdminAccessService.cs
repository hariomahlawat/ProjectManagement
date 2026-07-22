using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public sealed class MediaAdminAccessService : IMediaAdminAccessService
{
    private readonly IAuthorizationService _authorization;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MediaAdminAccessService(
        IAuthorizationService authorization,
        IHttpContextAccessor httpContextAccessor)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public async Task<bool> IsAuthorizedAsync(
        string policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        cancellationToken.ThrowIfCancellationRequested();

        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var result = await _authorization.AuthorizeAsync(user, null, policy);
        return result.Succeeded;
    }
}
