using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Document covers
// A cover is a small JPEG of a PDF's first page, drawn by Poppler's pdftoppm the first time it is
// asked for and kept beside the library files (<RootPath>/covers). The library shows a cover
// coloured by document type whenever no cover exists, so a server without Poppler, an encrypted
// PDF or a damaged file all degrade to that fallback rather than an error.
public interface IDocRepoCoverService
{
    // Full path of the cover JPEG, drawing it first if needed; null when there is none.
    Task<string?> GetCoverAsync(Document document, CancellationToken cancellationToken);

    void DeleteCover(Guid documentId);
}

public sealed class DocRepoCoverService : IDocRepoCoverService
{
    // Width in pixels: twice the largest cover the pages show, for sharp high-density screens.
    public const int CoverWidth = 360;

    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(30);
    // A PDF that could not be drawn is tried again after this long (for example once Poppler is
    // installed or upgraded).
    private static readonly TimeSpan FailureRetryAfter = TimeSpan.FromHours(12);
    // When pdftoppm cannot be started at all, covers are not attempted again for this long.
    private static readonly TimeSpan ToolRetryAfter = TimeSpan.FromMinutes(10);

    private readonly IDocStorage _storage;
    private readonly ILogger<DocRepoCoverService> _logger;
    private readonly string _coverRoot;
    private readonly string _executable;
    private readonly bool _enabled;
    private readonly SemaphoreSlim _renderSlots = new(2, 2);
    private readonly ConcurrentDictionary<Guid, Lazy<Task<string?>>> _inFlight = new();
    private DateTime _toolUnavailableUntilUtc = DateTime.MinValue;

    public DocRepoCoverService(
        IOptions<DocRepoOptions> options,
        IWebHostEnvironment environment,
        IDocStorage storage,
        ILogger<DocRepoCoverService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        var value = options.Value;

        _storage = storage;
        _logger = logger;
        _enabled = value.EnableCovers;
        _executable = string.IsNullOrWhiteSpace(value.PdfToPpmPath) ? "pdftoppm" : value.PdfToPpmPath.Trim();
        var root = string.IsNullOrWhiteSpace(value.RootPath) ? Path.Combine("App_Data", "DocRepo") : value.RootPath;
        root = Environment.ExpandEnvironmentVariables(root);
        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(environment.ContentRootPath, root);
        }

        _coverRoot = Path.Combine(Path.GetFullPath(root), "covers");
    }

    public async Task<string?> GetCoverAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_enabled)
        {
            return null;
        }

        var coverPath = CoverPath(document.Id);
        if (File.Exists(coverPath))
        {
            return coverPath;
        }

        var failedMarker = coverPath + ".failed";
        if (File.Exists(failedMarker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(failedMarker) < FailureRetryAfter)
        {
            return null;
        }

        if (DateTime.UtcNow < _toolUnavailableUntilUtc)
        {
            return null;
        }

        // One drawing per document at a time; later callers share its result.
        var work = _inFlight.GetOrAdd(document.Id, _ => new Lazy<Task<string?>>(() => RenderAsync(document, coverPath, failedMarker)));
        try
        {
            return await work.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (work.IsValueCreated && work.Value.IsCompleted)
            {
                _inFlight.TryRemove(new KeyValuePair<Guid, Lazy<Task<string?>>>(document.Id, work));
            }
        }
    }

    public void DeleteCover(Guid documentId)
    {
        var coverPath = CoverPath(documentId);
        foreach (var path in new[] { coverPath, coverPath + ".failed" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete the cover file {Path}.", path);
            }
        }
    }

    private string CoverPath(Guid documentId)
    {
        var name = documentId.ToString("N");
        return Path.Combine(_coverRoot, name[..2], name + ".jpg");
    }

    private async Task<string?> RenderAsync(Document document, string coverPath, string failedMarker)
    {
        // The drawing is not tied to one request: a visitor leaving the page must not waste it.
        await _renderSlots.WaitAsync();
        string? tempSource = null;
        var workPrefix = Path.Combine(Path.GetDirectoryName(coverPath)!, $"{document.Id:N}-{Guid.NewGuid():N}");
        try
        {
            if (File.Exists(coverPath))
            {
                return coverPath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(coverPath)!);

            var source = _storage.TryGetLocalPath(document.StoragePath);
            if (source is null || !File.Exists(source))
            {
                if (!await _storage.ExistsAsync(document.StoragePath, CancellationToken.None))
                {
                    return null;
                }

                tempSource = workPrefix + ".pdf";
                await using (var input = await _storage.OpenReadAsync(document.StoragePath, CancellationToken.None))
                await using (var output = File.Create(tempSource))
                {
                    await input.CopyToAsync(output);
                }

                source = tempSource;
            }

            var outcome = await RunPdfToPpmAsync(source, workPrefix);
            var produced = workPrefix + ".jpg";
            if (outcome == RenderOutcome.Drawn && File.Exists(produced))
            {
                File.Move(produced, coverPath, overwrite: true);
                return coverPath;
            }

            if (outcome == RenderOutcome.ToolMissing)
            {
                _toolUnavailableUntilUtc = DateTime.UtcNow + ToolRetryAfter;
                return null;
            }

            await File.WriteAllTextAsync(failedMarker, DateTime.UtcNow.ToString("O"));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not draw the cover for document {DocumentId}.", document.Id);
            return null;
        }
        finally
        {
            _renderSlots.Release();
            TryDelete(tempSource);
            TryDelete(workPrefix + ".jpg");
        }
    }

    private enum RenderOutcome { Drawn, Failed, ToolMissing }

    private async Task<RenderOutcome> RunPdfToPpmAsync(string sourcePdf, string outputPrefix)
    {
        var start = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // First page only, as one JPEG, CoverWidth pixels wide with the height kept in proportion.
        foreach (var argument in new[]
                 {
                     "-f", "1", "-l", "1", "-singlefile",
                     "-jpeg", "-jpegopt", "quality=82",
                     "-scale-to-x", CoverWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), "-scale-to-y", "-1",
                     sourcePdf, outputPrefix
                 })
        {
            start.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "pdftoppm ({Executable}) could not be started, so document covers are off. Install Poppler or set DocRepo:PdfToPpmPath.", _executable);
            return RenderOutcome.ToolMissing;
        }

        if (process is null)
        {
            return RenderOutcome.ToolMissing;
        }

        using (process)
        {
            var stderr = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(RenderTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                _logger.LogWarning("pdftoppm took longer than {Seconds}s on {Source}; no cover.", RenderTimeout.TotalSeconds, sourcePdf);
                return RenderOutcome.Failed;
            }

            if (process.ExitCode != 0)
            {
                _logger.LogInformation("pdftoppm could not draw {Source} (exit {ExitCode}): {Error}", sourcePdf, process.ExitCode, (await stderr).Trim());
                return RenderOutcome.Failed;
            }

            return RenderOutcome.Drawn;
        }
    }

    private void TryDelete(string? path)
    {
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete the temporary file {Path}.", path);
        }
    }
}
