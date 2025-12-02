using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services;
using ProjectManagement.Services.Security;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests;

public sealed class ProtectedFileUrlBuilderTests
{
    [Fact]
    public void CreateInlineUrl_UsesPathBase_WhenPresent()
    {
        // SECTION: Arrange
        var tokenService = new StubFileAccessTokenService("token-123");
        var userContext = new StubUserContext();
        var options = Options.Create(new FileDownloadOptions { BindTokensToUser = false });
        var httpContext = new DefaultHttpContext { Request = { PathBase = new PathString("/pm") } };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var builder = new ProtectedFileUrlBuilder(tokenService, userContext, options, accessor);

        // SECTION: Act
        var inlineUrl = builder.CreateInlineUrl("activities/1/example.pdf", "example.pdf", "application/pdf");

        // SECTION: Assert
        Assert.Equal("/pm/files?t=token-123&mode=inline", inlineUrl);
    }

    [Fact]
    public void CreateDownloadUrl_OmitsPathBase_WhenMissing()
    {
        // SECTION: Arrange
        var tokenService = new StubFileAccessTokenService("token-abc");
        var userContext = new StubUserContext();
        var options = Options.Create(new FileDownloadOptions { BindTokensToUser = false });
        var accessor = new HttpContextAccessor();
        var builder = new ProtectedFileUrlBuilder(tokenService, userContext, options, accessor);

        // SECTION: Act
        var downloadUrl = builder.CreateDownloadUrl("activities/2/photo.jpg", "photo.jpg", "image/jpeg");

        // SECTION: Assert
        Assert.Equal("/files?t=token-abc", downloadUrl);
    }

    private sealed class StubFileAccessTokenService : IFileAccessTokenService
    {
        private readonly string _token;

        public StubFileAccessTokenService(string token)
        {
            _token = token;
        }

        public string CreateToken(FileAccessTokenRequest request)
        {
            return _token;
        }

        public bool TryValidate(string token, out FileAccessTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public ClaimsPrincipal User { get; } = new(new ClaimsIdentity());

        public string? UserId => null;
    }
}
