using System;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Security;

namespace ProjectManagement.Services.Storage;

public interface IProtectedFileUrlBuilder
{
    string CreateDownloadUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null);

    string CreateInlineUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null);
}

public sealed class ProtectedFileUrlBuilder : IProtectedFileUrlBuilder
{
    private readonly IFileAccessTokenService _tokenService;
    private readonly IUserContext _userContext;
    private readonly FileDownloadOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProtectedFileUrlBuilder(
        IFileAccessTokenService tokenService,
        IUserContext userContext,
        IOptions<FileDownloadOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public string CreateDownloadUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null)
    {
        return CreateUrl("files", storageKey, fileName, contentType, lifetime, inline: false);
    }

    public string CreateInlineUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null)
    {
        return CreateUrl("files", storageKey, fileName, contentType, lifetime, inline: true);
    }

    // SECTION: URL creation helpers
    private string CreateUrl(string basePath, string storageKey, string? fileName, string? contentType, TimeSpan? lifetime, bool inline)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return string.Empty;
        }

        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var token = _tokenService.CreateToken(new FileAccessTokenRequest(
            normalized,
            _options.BindTokensToUser ? _userContext.UserId : null,
            fileName,
            contentType,
            lifetime));

        var encoded = UrlEncoder.Default.Encode(token);
        var trimmedBasePath = basePath.Trim('/');
        var pathBase = _httpContextAccessor.HttpContext?.Request.PathBase ?? PathString.Empty;
        var normalizedBase = pathBase.HasValue ? pathBase.Value!.TrimEnd('/') : string.Empty;

        var url = $"{normalizedBase}/{trimmedBasePath}?t={encoded}";
        if (inline)
        {
            url += "&mode=inline";
        }

        return url;
    }
}
