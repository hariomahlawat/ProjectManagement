using System.Security.Claims;

namespace ProjectManagement.Services;

public interface IUserContext
{
    ClaimsPrincipal User { get; }

    string? UserId { get; }
}
