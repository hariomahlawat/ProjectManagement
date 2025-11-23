using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Ocr;

namespace ProjectManagement.Services.Projects;

// SECTION: Project document OCR runner implementation using shared pipeline
public sealed class OcrmypdfProjectOcrRunner : IProjectDocumentOcrRunner
{
    // SECTION: Dependencies
    private readonly IProjectDocumentStorageResolver _storageResolver;
    private readonly string _ocrExecutable;
    private readonly string _workRoot;
    private readonly string _inputDir;
    private readonly string _outputDir;
    private readonly string _logsDir;
    private readonly OcrmypdfSharedRunner _sharedRunner;

    // SECTION: Constructor
    public OcrmypdfProjectOcrRunner(
        IProjectDocumentStorageResolver storageResolver,
        IOptions<ProjectDocumentOcrOptions> options,
        OcrmypdfSharedRunner sharedRunner)
    {
        _storageResolver = storageResolver ?? throw new ArgumentNullException(nameof(storageResolver));
        _sharedRunner = sharedRunner ?? throw new ArgumentNullException(nameof(sharedRunner));
        var value = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _ocrExecutable = ResolveExecutablePath(value.OcrExecutablePath);
        _workRoot = EnsureDirectory(ResolveWorkRoot(value));
        _inputDir = EnsureDirectory(Path.Combine(_workRoot, ResolveSubpath(value.InputSubpath, "input", nameof(value.InputSubpath))), _workRoot);
        _outputDir = EnsureDirectory(Path.Combine(_workRoot, ResolveSubpath(value.OutputSubpath, "output", nameof(value.OutputSubpath))), _workRoot);
        _logsDir = EnsureDirectory(Path.Combine(_workRoot, ResolveSubpath(value.LogsSubpath, "logs", nameof(value.LogsSubpath))), _workRoot);
    }

    // SECTION: OCR entry point
    public async Task<ProjectDocumentOcrResult> RunAsync(ProjectDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sourcePath = _storageResolver.ResolveAbsolutePath(document.StorageKey);
        if (!File.Exists(sourcePath))
        {
            return ProjectDocumentOcrResult.Failure($"Source file not found at '{sourcePath}'.");
        }

        var documentId = document.Id.ToString();
        var workInput = Path.Combine(_inputDir, $"{documentId}-{Guid.NewGuid():N}-source.pdf");

        try
        {
            // SECTION: Copy source PDF to OCR work directory
            File.Copy(sourcePath, workInput, overwrite: true);

            // SECTION: Execute shared OCR pipeline
            var request = new OcrmypdfSharedRequest
            {
                DocumentId = documentId,
                OcrExecutable = _ocrExecutable,
                WorkRoot = _workRoot,
                InputDirectory = _inputDir,
                OutputDirectory = _outputDir,
                LogsDirectory = _logsDir,
                SourcePdfPath = workInput,
                SourceAlreadyInWorkDirectory = true
            };

            var result = await _sharedRunner.RunAsync(request, cancellationToken);
            return result.Success
                ? ProjectDocumentOcrResult.SuccessResult(result.Text!)
                : ProjectDocumentOcrResult.Failure(result.Error ?? "OCR failed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProjectDocumentOcrResult.Failure($"OCR failed: {ex.Message}.");
        }
        finally
        {
            // SECTION: Cleanup temporary artifacts
            TryDelete(workInput);
        }
    }

    // SECTION: Executable resolution
    private static string ResolveExecutablePath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return "ocrmypdf";
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
        var fullPath = Path.GetFullPath(expanded);

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Configured ocrmypdf executable was not found at '{fullPath}'.");
        }

        return fullPath;
    }

    // SECTION: Directory helpers
    private static string EnsureDirectory(string path, string? root = null)
    {
        var fullPath = Path.GetFullPath(path);

        if (!string.IsNullOrEmpty(root))
        {
            var normalizedRoot = Path.GetFullPath(root);
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The path '{fullPath}' must reside inside the configured work root '{normalizedRoot}'.");
            }
        }

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static string ResolveWorkRoot(ProjectDocumentOcrOptions options)
    {
        var configured = (options.WorkRoot ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(configured))
        {
            throw new InvalidOperationException(
                "ProjectDocumentOcrOptions.WorkRoot must be configured. " +
                "Set the 'ProjectDocumentOcr:WorkRoot' configuration value to the desired directory.");
        }

        configured = Environment.ExpandEnvironmentVariables(configured);

        if (configured.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                throw new InvalidOperationException(
                    "WorkRoot paths starting with '~' require a resolvable user profile directory.");
            }

            var remainder = configured[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            configured = Path.Combine(home, remainder);
        }

        if (!Path.IsPathRooted(configured))
        {
            configured = Path.GetFullPath(configured, AppContext.BaseDirectory);
        }

        return configured;
    }

    private static string ResolveSubpath(string? configured, string fallback, string propertyName)
    {
        var trimmed = configured?.Trim();
        var candidate = string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
        var sanitized = candidate.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException($"The subpath for '{propertyName}' cannot be empty.");
        }

        if (Path.IsPathRooted(sanitized))
        {
            throw new InvalidOperationException($"The subpath for '{propertyName}' must be relative.");
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        foreach (var segment in sanitized.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "." || segment == "..")
            {
                throw new InvalidOperationException($"The subpath for '{propertyName}' cannot contain directory traversal segments.");
            }
        }

        return sanitized;
    }

    // SECTION: Cleanup helpers
    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup; ignore failures.
        }
    }
}
