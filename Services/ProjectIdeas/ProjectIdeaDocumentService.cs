using Microsoft.EntityFrameworkCore;
using ProjectManagement.Services.Storage;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaDocumentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".png", ".jpg", ".jpeg" };
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
        if (file.Length <= 0) return (false, "Select a file to upload.");
        if (file.Length > MaxFileSizeBytes) return (false, "File size must be 20 MB or less.");
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension)) return (false, "This file type is not allowed.");
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var relativeFolder = Path.Combine("ProjectIdeas", idea.Id.ToString(), "Documents");
        var absoluteFolder = Path.Combine(_uploadRootProvider.RootPath, relativeFolder);
        Directory.CreateDirectory(absoluteFolder);
        var absolutePath = Path.Combine(absoluteFolder, storedFileName);
        await using (var stream = File.Create(absolutePath)) { await file.CopyToAsync(stream); }
        _db.ProjectIdeaDocuments.Add(new ProjectIdeaDocument { ProjectIdeaId = idea.Id, OriginalFileName = Path.GetFileName(file.FileName), StoredFileName = storedFileName, FilePath = Path.Combine(relativeFolder, storedFileName).Replace('\\', '/'), ContentType = file.ContentType, FileSizeBytes = file.Length, UploadedByUserId = userId });
        idea.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public Task<ProjectIdeaDocument?> GetAsync(int id) => _db.ProjectIdeaDocuments.Include(x => x.ProjectIdea).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    public async Task SoftDeleteAsync(ProjectIdeaDocument document) { document.IsDeleted = true; await _db.SaveChangesAsync(); }
    public string GetAbsolutePath(ProjectIdeaDocument document) => Path.Combine(_uploadRootProvider.RootPath, document.FilePath.Replace('/', Path.DirectorySeparatorChar));
}
