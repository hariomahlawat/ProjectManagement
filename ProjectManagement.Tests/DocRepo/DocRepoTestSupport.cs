using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>Shared builders for the Document Repository page and service tests.</summary>
internal static class DocRepoTestSupport
{
    public const string ManagerId = "hod-user";
    public const string ReaderId = "reader-user";

    public static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>A HoD: may manage and see deactivated documents.</summary>
    public static ClaimsPrincipal Manager => User(ManagerId, RoleNames.HoD);

    /// <summary>A signed-in user with no repository role.</summary>
    public static ClaimsPrincipal Reader => User(ReaderId);

    public static ClaimsPrincipal User(string id, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, id) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>The real repository policies, as registered in Program.cs.</summary>
    public static IAuthorizationService Authorization()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(options =>
            {
                options.AddPolicy(Policies.Documents.View, policy => policy.RequireAuthenticatedUser());
                options.AddPolicy(Policies.Documents.EditMetadata, policy => policy.RequireRole(Policies.Documents.MetadataEditorRoles));
                options.AddPolicy(Policies.Documents.SoftDelete, policy => policy.RequireRole(Policies.Documents.UploadAndSoftDeleteRoles));
                options.AddPolicy(Policies.Documents.ViewInactive, policy => policy.RequireRole(Policies.Documents.InactiveViewerRoles));
                options.AddPolicy(Policies.Documents.Upload, policy => policy.RequireRole(Policies.Documents.UploadAndSoftDeleteRoles));
                options.AddPolicy(Policies.Documents.DeleteApprove, policy => policy.RequireRole(Policies.Documents.DeleteApprovalRoles));
                options.AddPolicy(Policies.Documents.Purge, policy => policy.RequireRole(Policies.Documents.PurgeRoles));
                options.AddPolicy(Policies.Documents.ManageCategories, policy => policy.RequireRole(Policies.Documents.CategoryManagerRoles));
            })
            .BuildServiceProvider();
        return services.GetRequiredService<IAuthorizationService>();
    }

    public static IDocRepoAccess Access() => new DocRepoAccess(Authorization());

    public static void ConfigurePage(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
    }

    public static (OfficeCategory Office, DocumentCategory Category) SeedLookups(ApplicationDbContext db)
    {
        var office = new OfficeCategory { Id = 1, Name = "CPDS North" };
        var category = new DocumentCategory { Id = 1, Name = "Letter" };
        db.OfficeCategories.Add(office);
        db.DocumentCategories.Add(category);
        return (office, category);
    }

    public static Document SeedDocument(
        ApplicationDbContext db,
        string subject = "Document",
        bool isActive = true,
        bool isDeleted = false,
        bool isExternal = false,
        string? sha256 = null)
    {
        var id = Guid.NewGuid();
        var document = new Document
        {
            Id = id,
            Subject = subject,
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            OriginalFileName = $"{subject}.pdf",
            FileSizeBytes = 10,
            Sha256 = sha256 ?? id.ToString("N"),
            StoragePath = $"2026/10/{id:N}.pdf",
            IsActive = isActive,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? DateTime.UtcNow.AddDays(-1) : null,
            IsExternal = isExternal,
            CreatedByUserId = "uploader",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        db.Documents.Add(document);
        return document;
    }

    public static byte[] PdfBytes(string body = "test")
        => Encoding.ASCII.GetBytes($"%PDF-1.4\n% {body}\n%%EOF\n");

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}

/// <summary>In-memory IDocStorage that records what was saved and deleted.</summary>
internal sealed class RecordingDocStorage : IDocStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public List<string> Saved { get; } = new();
    public List<string> Deleted { get; } = new();

    public void Put(string storagePath, byte[] bytes) => _files[storagePath] = bytes;

    public async Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        var path = $"{utcNow:yyyy}/{utcNow:MM}/{Guid.NewGuid():N}.pdf";
        _files[path] = buffer.ToArray();
        Saved.Add(path);
        return path;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
        => _files.TryGetValue(storagePath, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(storagePath);

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        Deleted.Add(storagePath);
        _files.Remove(storagePath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
        => Task.FromResult(_files.ContainsKey(storagePath));
}
