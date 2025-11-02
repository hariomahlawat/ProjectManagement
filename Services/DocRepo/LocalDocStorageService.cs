using Microsoft.Extensions.Options;

namespace ProjectManagement.Services.DocRepo;

public class DocRepoOptions
{
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "DocRepo");
}

public class LocalDocStorageService : IDocStorage
{
    private readonly DocRepoOptions _options;

    public LocalDocStorageService(IOptions<DocRepoOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct)
    {
        var year = utcNow.ToString("yyyy");
        var month = utcNow.ToString("MM");
        var folder = Path.Combine(_options.RootPath, year, month);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(folder, fileName);

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.CopyToAsync(fs, ct);
        }

        return Path.GetRelativePath(_options.RootPath, fullPath);
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_options.RootPath, storagePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = Path.Combine(_options.RootPath, storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
