using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Security;
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
    private readonly IFileSecurityValidator _fileSecurityValidator;

    public ProjectIdeaDocumentService(ApplicationDbContext db, IUploadRootProvider uploadRootProvider, IFileSecurityValidator fileSecurityValidator)
    {
        _db = db;
        _uploadRootProvider = uploadRootProvider;
        _fileSecurityValidator = fileSecurityValidator;
    }

    // SECTION: Upload and storage
    public async Task<(bool Success, string? Error)> UploadAsync(ProjectIdea idea, IFormFile file, string userId)
    {
        if (idea is null)
        {
            return (false, "Idea not found.");
        }

        if (file is null || file.Length == 0)
        {
            return (false, "Please select a valid document.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (false, "File size must be 20 MB or less.");
        }

        var originalFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            originalFileName = "document";
        }

        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return (false, "This file type is not allowed.");
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (!string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) &&
            !AllowedContentTypes.Contains(contentType))
        {
            return (false, "The uploaded file content type is not allowed.");
        }

        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativeFolder = Path.Combine("ProjectIdeas", idea.Id.ToString(), "Documents");
        var relativePath = Path.Combine(relativeFolder, storedFileName).Replace('\\', '/');
        if (relativePath.Length > 500)
        {
            return (false, "Generated storage path is too long.");
        }

        var root = Path.GetFullPath(_uploadRootProvider.RootPath);
        var protectedRoot = EnsureTrailingSeparator(root);
        var absoluteFolder = Path.GetFullPath(Path.Combine(root, relativeFolder));
        var absolutePath = Path.GetFullPath(Path.Combine(absoluteFolder, storedFileName));
        if (!absoluteFolder.StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase)
            || !absolutePath.StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Invalid storage path.");
        }

        var tempFile = string.Empty;

        try
        {
            _fileSecurityValidator.ValidateRelativePath(relativePath);
            tempFile = Path.GetTempFileName();
            await using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.CopyToAsync(stream);
            }

            if (!await _fileSecurityValidator.IsSafeAsync(tempFile, contentType))
            {
                SafeDelete(tempFile);
                return (false, "File failed security checks.");
            }

            Directory.CreateDirectory(absoluteFolder);
            File.Move(tempFile, absolutePath, overwrite: true);
            tempFile = string.Empty;

            _db.ProjectIdeaDocuments.Add(new ProjectIdeaDocument
            {
                ProjectIdeaId = idea.Id,
                OriginalFileName = Truncate(originalFileName, 255),
                StoredFileName = Truncate(storedFileName, 255),
                FilePath = relativePath,
                ContentType = Truncate(contentType, 100),
                FileSizeBytes = file.Length,
                UploadedByUserId = userId,
                UploadedAt = DateTime.UtcNow,
                IsDeleted = false
            });

            idea.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, null);
        }
        catch
        {
            SafeDelete(tempFile);
            SafeDelete(absolutePath);
            return (false, "Document upload failed. Please try again.");
        }
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
        var protectedRoot = EnsureTrailingSeparator(root);
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

    // SECTION: File helper utilities
    private static string EnsureTrailingSeparator(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Suppress cleanup errors deliberately. */ }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
