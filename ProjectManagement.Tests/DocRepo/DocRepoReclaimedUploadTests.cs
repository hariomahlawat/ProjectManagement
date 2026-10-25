using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.DocumentRepository.Pages.Documents;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using Xunit;

namespace ProjectManagement.Tests.DocRepo;

// A copy trashed by an IPR delete, then uploaded to the repository by a user, belongs to the
// repository from then on: a later IPR attach-and-remove must not trash it again.
public sealed class DocRepoReclaimedUploadTests
{
    [Fact]
    public async Task UploadingATrashedIngestedCopy_MakesItARepositoryDocumentThatSourceDeletesKeep()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.OfficeCategories.Add(new OfficeCategory { Id = 1, Name = "Office" });
        db.DocumentCategories.Add(new DocumentCategory { Id = 1, Name = "Letters" });

        var pdfBytes = PdfBytes();
        var documentId = Guid.NewGuid();
        db.Documents.Add(new Document
        {
            Id = documentId,
            Subject = "evidence",
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            OriginalFileName = "evidence.pdf",
            FileSizeBytes = pdfBytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(pdfBytes)),
            StoragePath = "docs/evidence.pdf",
            IsExternal = true,
            IsDeleted = true,
            IsActive = false,
            CreatedByUserId = "system",
            CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.DocRepoAudits.Add(new DocRepoAudit
        {
            DocumentId = documentId,
            ActorUserId = "editor",
            EventType = DocRepoSourceLinks.SourceRemovedEvent,
            OccurredAtUtc = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var page = CreatePage(db, pdfBytes);
        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var reclaimed = await db.Documents.AsNoTracking().SingleAsync();
        Assert.Equal(documentId, reclaimed.Id);
        Assert.False(reclaimed.IsDeleted);
        Assert.False(reclaimed.IsExternal);

        // The same PDF is attached to IPR again, then removed there.
        db.DocRepoExternalLinks.Add(new DocRepoExternalLink
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            SourceModule = "IPR",
            SourceItemId = "50",
            CreatedAtUtc = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var trashed = await DocRepoSourceLinks.StageRemovalAsync(
            db, "IPR", new[] { "50" }, "editor", "IPR attachment evidence.pdf was removed.",
            new DateTimeOffset(2024, 1, 4, 0, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        Assert.Empty(trashed);
        Assert.False((await db.Documents.AsNoTracking().SingleAsync()).IsDeleted);
        Assert.Empty(await db.DocRepoExternalLinks.AsNoTracking().ToListAsync());
    }

    private static UploadModel CreatePage(ApplicationDbContext db, byte[] pdfBytes)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "user-9") }, "test"))
        };

        var file = new FormFile(new MemoryStream(pdfBytes), 0, pdfBytes.Length, "Input.File", "evidence.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        return new UploadModel(
            db,
            new MemoryDocStorage(),
            new NoopFileScanner(),
            new DocRepoAuditService(db),
            new DenyAuthorizationService(),
            Options.Create(new ProjectDocumentOptions()),
            Options.Create(new DocumentUploadRateLimitOptions()))
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider()),
            Input = new UploadModel.InputModel
            {
                Subject = "Evidence letter",
                OfficeCategoryId = 1,
                DocumentCategoryId = 1,
                File = file
            }
        };
    }

    private static byte[] PdfBytes()
    {
        using var stream = IprWriteServiceTests.CreateValidPdfStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class MemoryDocStorage : IDocStorage
    {
        public Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct)
            => Task.FromResult($"docs/{Guid.NewGuid():N}.pdf");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string storagePath, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class DenyAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
