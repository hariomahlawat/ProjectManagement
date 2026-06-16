using Microsoft.EntityFrameworkCore;
using ProjectManagement.Services.Storage;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaDocumentService
{
    // SECTION: Upload validation configuration
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".png", ".jpg", ".jpeg"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "image/png",
        "image/jpeg"
    };

    private const long MaxFileSizeBytes = 20 * 1024 * 1024;
    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRootProvider;

    public ProjectIdeaDocumentService(ApplicationDbContext db, IUploadRootProvider uploadRootProvider)
    {
        _db = db;
        _uploadRootProvider = uploadRootProvider;
    }

    // SECTION: Upload and storage
    public async Task<(bool Success, string? Error)> UploadAsync(ProjectIdea idea, IFormFile file, string userId)
    {
        if (file is null || file.Length == 0)
        {
            return (false, "Please select a valid file.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (false, "File size must be 20 MB or less.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return (false, "This file type is not allowed.");
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) && !AllowedContentTypes.Contains(file.ContentType))
        {
            return (false, "The uploaded file content type is not allowed.");
        }

        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativeFolder = Path.Combine("ProjectIdeas", idea.Id.ToString(), "Documents");
        var absoluteFolder = Path.Combine(_uploadRootProvider.RootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);
        var absolutePath = Path.Combine(absoluteFolder, storedFileName);

        await using (var stream = File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        _db.ProjectIdeaDocuments.Add(new ProjectIdeaDocument
        {
            ProjectIdeaId = idea.Id,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            FilePath = Path.Combine(relativeFolder, storedFileName).Replace('\\', '/'),
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedByUserId = userId
        });

        idea.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // SECTION: Document lookup and deletion
    public Task<ProjectIdeaDocument?> GetAsync(int id)
    {
        return _db.ProjectIdeaDocuments.Include(x => x.ProjectIdea).FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task SoftDeleteAsync(ProjectIdeaDocument document)
    {
        document.IsDeleted = true;

        var idea = await _db.ProjectIdeas.FirstOrDefaultAsync(i => i.Id == document.ProjectIdeaId);
        if (idea is not null)
        {
            idea.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    // SECTION: Protected path resolution
    public string GetAbsolutePath(ProjectIdeaDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            throw new InvalidOperationException("Document file path is missing.");
        }

        var root = Path.GetFullPath(_uploadRootProvider.RootPath);
        var protectedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var relativePath = document.FilePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!fullPath.StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid document storage path.");
        }

        return fullPath;
    }
}
