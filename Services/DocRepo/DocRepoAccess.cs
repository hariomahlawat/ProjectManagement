using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Contract
/// <summary>
/// The one place that decides which repository documents a user may read. Every read path
/// (list, reader, inline view, download, favourites) filters through <see cref="Readable"/>.
/// Deleted documents are never readable here; deactivated documents are readable only by
/// holders of <see cref="Policies.Documents.ViewInactive"/>. Documents copied in from other
/// modules (IsExternal) are readable by every signed-in user, as documented in
/// docs/document-repository.md.
/// </summary>
public interface IDocRepoAccess
{
    Task<bool> CanViewInactiveAsync(ClaimsPrincipal user);

    IQueryable<Document> Readable(IQueryable<Document> documents, bool canViewInactive);
}

// SECTION: Implementation
public sealed class DocRepoAccess : IDocRepoAccess
{
    private readonly IAuthorizationService _authorization;
    private readonly Dictionary<ClaimsPrincipal, bool> _cache = new();

    public DocRepoAccess(IAuthorizationService authorization)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async Task<bool> CanViewInactiveAsync(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_cache.TryGetValue(user, out var cached))
        {
            return cached;
        }

        var allowed = (await _authorization.AuthorizeAsync(user, Policies.Documents.ViewInactive)).Succeeded;
        _cache[user] = allowed;
        return allowed;
    }

    public IQueryable<Document> Readable(IQueryable<Document> documents, bool canViewInactive)
        => ReadableFilter(documents, canViewInactive);

    /// <summary>Exposed for callers without a user context, such as background search indexing.</summary>
    public static IQueryable<Document> ReadableFilter(IQueryable<Document> documents, bool canViewInactive)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return canViewInactive
            ? documents.Where(document => !document.IsDeleted)
            : documents.Where(document => !document.IsDeleted && document.IsActive);
    }
}
