using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.Security;

public sealed class FileAccessTokenService : IFileAccessTokenService
{
    private readonly IDataProtector _protector;
    private readonly FileDownloadOptions _options;

    public FileAccessTokenService(IDataProtectionProvider dataProtectionProvider, IOptions<FileDownloadOptions> options)
    {
        _protector = (dataProtectionProvider ?? throw new ArgumentNullException(nameof(dataProtectionProvider)))
            .CreateProtector("FileAccessTokenService");
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string CreateToken(FileAccessTokenRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var lifetime = request.Lifetime ?? TimeSpan.FromMinutes(Math.Max(1, _options.TokenLifetimeMinutes));
        var payload = new InternalPayload
        {
            StorageKey = request.StorageKey,
            UserId = request.UserId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime)
        };

        var json = JsonSerializer.Serialize(payload);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));
        return WebEncoders.Base64UrlEncode(protectedBytes);
    }

    public bool TryValidate(string token, out FileAccessTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(token);
            var json = Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
            var internalPayload = JsonSerializer.Deserialize<InternalPayload>(json);
            if (internalPayload is null)
            {
                return false;
            }

            if (internalPayload.ExpiresAtUtc < DateTimeOffset.UtcNow)
            {
                return false;
            }

            payload = new FileAccessTokenPayload(
                internalPayload.StorageKey,
                internalPayload.UserId,
                internalPayload.FileName,
                internalPayload.ContentType,
                internalPayload.ExpiresAtUtc);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class InternalPayload
    {
        public string StorageKey { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}
