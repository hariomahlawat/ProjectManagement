using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Services;

public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
}
