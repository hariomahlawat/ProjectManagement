using Microsoft.Extensions.Options;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Options
public class DocRepoOptions
{
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "DocRepo");
    public bool EnableOcrWorker { get; set; } = true;
    public bool EnableIngestion { get; set; } = true;
    public int? IngestionOfficeCategoryId { get; set; }
        = null;
    public int? IngestionDocumentCategoryId { get; set; }
        = null;
    public string IngestionUserId { get; set; } = "system";
    public long MaxFileSizeBytes { get; set; } = 52_428_800;
    public string? OcrWorkRoot { get; set; }
    public string OcrInput { get; set; } = "input";
    public string OcrOutput { get; set; } = "output";
    public string OcrLogs { get; set; } = "logs";
}

// SECTION: Local storage implementation
public class LocalDocStorageService : IDocStorage
{
    private readonly string _rootPath;

    public LocalDocStorageService(IOptions<DocRepoOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value ?? throw new ArgumentException("DocRepo options cannot be null.", nameof(options));

        _rootPath = ResolveRootPath(value.RootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct)
    {
        var year = utcNow.ToString("yyyy");
        var month = utcNow.ToString("MM");
        var folder = Path.Combine(_rootPath, year, month);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(folder, fileName);

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.CopyToAsync(fs, ct);
        }

        return Path.GetRelativePath(_rootPath, fullPath);
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_rootPath, storagePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_rootPath, storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private static string ResolveRootPath(string? configuredRoot)
    {
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data", "DocRepo")
            : configuredRoot;

        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, root);
        }

        return Path.GetFullPath(root);
    }
}
