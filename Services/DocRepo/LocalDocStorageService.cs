using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Options
public class DocRepoOptions
{
    [Required]
    public string RootPath { get; set; } = Path.Combine("App_Data", "DocRepo");
    public bool EnableOcrWorker { get; set; } = true;
    public bool EnableIngestion { get; set; } = true;
    public int? IngestionOfficeCategoryId { get; set; }
        = null;
    public int? IngestionDocumentCategoryId { get; set; }
        = null;
    public string IngestionUserId { get; set; } = "system";
    // SECTION: Upload constraints
    public long MaxFileSizeBytes { get; set; } = 104_857_600;
    public string? OcrExecutablePath { get; set; }
    public string? OcrWorkRoot { get; set; }
    public string OcrInput { get; set; } = "input";
    public string OcrOutput { get; set; } = "output";
    public string OcrLogs { get; set; } = "logs";
}

// SECTION: Local storage implementation
public class LocalDocStorageService : IDocStorage
{
    private readonly string _rootPath;

    public LocalDocStorageService(IOptions<DocRepoOptions> options, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        var value = options.Value ?? throw new ArgumentException("DocRepo options cannot be null.", nameof(options));

        _rootPath = ResolveRootPath(value.RootPath, environment.ContentRootPath);
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

    public Task<bool> ExistsAsync(string storagePath, CancellationToken ct)
    {
        // SECTION: Validate input
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Task.FromResult(false);
        }

        // SECTION: Resolve full path
        var fullPath = Path.Combine(_rootPath, storagePath);

        // SECTION: Existence check (safe when directory is missing)
        return Task.FromResult(File.Exists(fullPath));
    }

    private static string ResolveRootPath(string? configuredRoot, string contentRoot)
    {
        // SECTION: Resolve configured path
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine("App_Data", "DocRepo")
            : configuredRoot;

        root = ExpandPath(root);

        // SECTION: Normalize relative paths to the content root
        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(contentRoot, root);
        }

        return Path.GetFullPath(root);
    }

    // SECTION: Path helpers
    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);

        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                var remainder = expanded[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                expanded = Path.Combine(home, remainder);
            }
        }

        return expanded;
    }
}
