using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ProjectManagement.Services.Documents;

public interface IDocumentPreviewTokenService
{
    string CreateToken(int documentId, string userId, IEnumerable<string> roles, DateTimeOffset expiresAtUtc, int fileStamp);

    bool TryValidate(string token, out DocumentPreviewTokenPayload payload);

    ClaimsPrincipal CreatePrincipal(DocumentPreviewTokenPayload payload);
}

public sealed class DocumentPreviewTokenService : IDocumentPreviewTokenService
{
    private const string ProtectorPurpose = "ProjectManagement.DocumentPreviewToken.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;

    public DocumentPreviewTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        if (dataProtectionProvider is null)
        {
            throw new ArgumentNullException(nameof(dataProtectionProvider));
        }

        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    }

    public string CreateToken(int documentId, string userId, IEnumerable<string> roles, DateTimeOffset expiresAtUtc, int fileStamp)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id must be provided.", nameof(userId));
        }

        if (roles is null)
        {
            throw new ArgumentNullException(nameof(roles));
        }

        var payload = new DocumentPreviewTokenPayload
        {
            DocumentId = documentId,
            UserId = userId,
            ExpiresAtUtc = expiresAtUtc,
            Roles = roles.ToArray(),
            FileStamp = fileStamp
        };

        var serialized = JsonSerializer.Serialize(payload, SerializerOptions);
        return _protector.Protect(serialized);
    }

    public bool TryValidate(string token, out DocumentPreviewTokenPayload payload)
    {
        payload = default!;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var unprotected = _protector.Unprotect(token);
            var result = JsonSerializer.Deserialize<DocumentPreviewTokenPayload>(unprotected, SerializerOptions);
            if (result is null)
            {
                return false;
            }

            payload = result;
            return true;
        }
        catch
        {
            payload = default!;
            return false;
        }
    }

    public ClaimsPrincipal CreatePrincipal(DocumentPreviewTokenPayload payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var identity = new ClaimsIdentity("DocumentPreviewToken");

        if (payload.Roles is not null)
        {
            foreach (var role in payload.Roles)
            {
                if (!string.IsNullOrWhiteSpace(role))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }

        return new ClaimsPrincipal(identity);
    }
}

public sealed class DocumentPreviewTokenPayload
{
    public int DocumentId { get; init; }

    public string UserId { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public int FileStamp { get; init; }
}
