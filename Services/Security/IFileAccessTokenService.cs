using System;

namespace ProjectManagement.Services.Security;

public sealed record FileAccessTokenRequest(
    string StorageKey,
    string? UserId,
    string? FileName,
    string? ContentType,
    TimeSpan? Lifetime = null);

public sealed record FileAccessTokenPayload(
    string StorageKey,
    string? UserId,
    string? FileName,
    string? ContentType,
    DateTimeOffset ExpiresAtUtc);

public interface IFileAccessTokenService
{
    string CreateToken(FileAccessTokenRequest request);

    bool TryValidate(string token, out FileAccessTokenPayload? payload);
}
