using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Documents;

namespace ProjectManagement.Services.Projects;

// SECTION: Project document OCR runner implementation
public sealed class OcrmypdfProjectOcrRunner : IProjectDocumentOcrRunner
{
    // SECTION: Dependencies
    private readonly IProjectDocumentStorageResolver _storageResolver;
    private readonly string _workRoot;
    private readonly string _inputDir;
    private readonly string _outputDir;
    private readonly string _logsDir;

    // SECTION: Constructor
    public OcrmypdfProjectOcrRunner(
        IProjectDocumentStorageResolver storageResolver,
        IOptions<ProjectDocumentOcrOptions> options)
    {
        _storageResolver = storageResolver ?? throw new ArgumentNullException(nameof(storageResolver));
        var value = options?.Value ?? throw new ArgumentNullException(nameof(options));

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
        var runToken = GenerateRunToken();
        var inputPdf = BuildTempPath(_inputDir, documentId, runToken, ".pdf");
        var outputPdf = BuildTempPath(_outputDir, documentId, runToken, ".pdf");
        var sidecar = BuildTempPath(_outputDir, documentId, runToken, ".txt");
        var logFile = BuildTempPath(_logsDir, documentId, runToken, ".log");
        var latestLog = Path.Combine(_logsDir, documentId + ".log");

        try
        {
            // SECTION: Copy source PDF to OCR work directory
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = File.Create(inputPdf))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            // SECTION: First OCR pass
            var first = await RunOcrmypdfAsync(
                workingDir: _workRoot,
                cancellationToken: cancellationToken,
                "--sidecar", sidecar, inputPdf, outputPdf);

            await File.WriteAllTextAsync(
                logFile,
                $"FIRST RUN exit={first.ExitCode}{Environment.NewLine}{first.Stdout}{Environment.NewLine}{first.Stderr}",
                cancellationToken);
            MirrorLogToLatest(logFile, latestLog);

            var isTaggedPdf = IsTaggedPdf(first);
            if (isTaggedPdf)
            {
                // SECTION: Skip-text OCR pass
                var skipText = await RunOcrmypdfAsync(
                    workingDir: _workRoot,
                    cancellationToken: cancellationToken,
                    "--skip-text", "--sidecar", sidecar, inputPdf, outputPdf);

                await File.AppendAllTextAsync(
                    logFile,
                    $"{Environment.NewLine}SECOND RUN (skip-text) exit={skipText.ExitCode}{Environment.NewLine}{skipText.Stdout}{Environment.NewLine}{skipText.Stderr}",
                    cancellationToken);
                MirrorLogToLatest(logFile, latestLog);

                if (!File.Exists(sidecar))
                {
                    return ProjectDocumentOcrResult.Failure($"ocrmypdf (skip-text) did not produce a sidecar file. See {logFile}");
                }

                var skipTextContent = await File.ReadAllTextAsync(sidecar, cancellationToken);
                if (HasUsefulText(skipTextContent))
                {
                    return ProjectDocumentOcrResult.SuccessResult(skipTextContent);
                }

                // SECTION: Skip-text fallback (force OCR)
                var forcedAfterSkip = await RunOcrmypdfAsync(
                    workingDir: _workRoot,
                    cancellationToken: cancellationToken,
                    "--force-ocr", "--sidecar", sidecar, inputPdf, outputPdf);

                await File.AppendAllTextAsync(
                    logFile,
                    $"{Environment.NewLine}THIRD RUN (force after skip-text) exit={forcedAfterSkip.ExitCode}{Environment.NewLine}{forcedAfterSkip.Stdout}{Environment.NewLine}{forcedAfterSkip.Stderr}",
                    cancellationToken);
                MirrorLogToLatest(logFile, latestLog);

                if (!File.Exists(sidecar))
                {
                    return ProjectDocumentOcrResult.Failure($"ocrmypdf (force after skip-text) did not produce a sidecar file. See {logFile}");
                }

                var forcedAfterSkipContent = await File.ReadAllTextAsync(sidecar, cancellationToken);
                if (!HasUsefulText(forcedAfterSkipContent))
                {
                    return ProjectDocumentOcrResult.Failure($"ocrmypdf (force after skip-text) produced unusable text. See {logFile}");
                }

                return ProjectDocumentOcrResult.SuccessResult(forcedAfterSkipContent);
            }

            var needForce =
                first.ExitCode == 6 ||
                (first.Stderr?.IndexOf("PriorOcrFoundError", StringComparison.OrdinalIgnoreCase) >= 0);

            if (needForce)
            {
                // SECTION: Forced OCR pass (prior OCR detected)
                return await RunForcedAsync(
                    logFile,
                    latestLog,
                    sidecar,
                    inputPdf,
                    outputPdf,
                    cancellationToken,
                    runLabel: "SECOND RUN (force)",
                    failureContext: "forced");
            }

            if (!File.Exists(sidecar))
            {
                return ProjectDocumentOcrResult.Failure($"ocrmypdf did not produce a sidecar file. Exit {first.ExitCode}. See {logFile}");
            }

            var text = await File.ReadAllTextAsync(sidecar, cancellationToken);
            if (HasUsefulText(text))
            {
                return ProjectDocumentOcrResult.SuccessResult(text);
            }

            // SECTION: Fallback when sidecar lacks useful text (native PDF)
            return await RunForcedAsync(
                logFile,
                latestLog,
                sidecar,
                inputPdf,
                outputPdf,
                cancellationToken,
                runLabel: "SECOND RUN (force after empty)",
                failureContext: "force after empty");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(logFile, ex.ToString(), cancellationToken);
            MirrorLogToLatest(logFile, latestLog);
            return ProjectDocumentOcrResult.Failure($"OCR failed: {ex.Message}. See {logFile}");
        }
        finally
        {
            // SECTION: Cleanup temporary artifacts
            TryDelete(inputPdf);
            TryDelete(outputPdf);
            TryDelete(sidecar);
        }
    }

    // SECTION: ocrmypdf invocation helpers
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunOcrmypdfAsync(
        string workingDir,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ocrmypdf",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        if (arguments.Length == 0)
        {
            throw new ArgumentException("At least one argument must be provided.", nameof(arguments));
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ocrmypdf process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    // SECTION: Forced OCR helper
    private async Task<ProjectDocumentOcrResult> RunForcedAsync(
        string logFile,
        string latestLog,
        string sidecar,
        string inputPdf,
        string outputPdf,
        CancellationToken cancellationToken,
        string runLabel,
        string failureContext)
    {
        var forced = await RunOcrmypdfAsync(
            workingDir: _workRoot,
            cancellationToken: cancellationToken,
            "--force-ocr", "--sidecar", sidecar, inputPdf, outputPdf);

        await File.AppendAllTextAsync(
            logFile,
            $"{Environment.NewLine}{runLabel} exit={forced.ExitCode}{Environment.NewLine}{forced.Stdout}{Environment.NewLine}{forced.Stderr}",
            cancellationToken);
        MirrorLogToLatest(logFile, latestLog);

        if (!File.Exists(sidecar))
        {
            return ProjectDocumentOcrResult.Failure($"ocrmypdf ({failureContext}) did not produce a sidecar file. See {logFile}");
        }

        var forcedText = await File.ReadAllTextAsync(sidecar, cancellationToken);
        if (!HasUsefulText(forcedText))
        {
            return ProjectDocumentOcrResult.Failure($"ocrmypdf ({failureContext}) produced unusable text. See {logFile}");
        }

        return ProjectDocumentOcrResult.SuccessResult(forcedText);
    }

    // SECTION: Result helpers
    private static bool HasUsefulText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.StartsWith("OCR skipped on page", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.StartsWith("Prior OCR", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsTaggedPdf((int ExitCode, string Stdout, string Stderr) result)
    {
        if (result.ExitCode != 2)
        {
            return false;
        }

        var stderr = result.Stderr ?? string.Empty;
        return stderr.IndexOf("TaggedPDFError", StringComparison.OrdinalIgnoreCase) >= 0 ||
               stderr.IndexOf("This PDF is marked as a Tagged PDF", StringComparison.OrdinalIgnoreCase) >= 0;
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

    // SECTION: Path helpers
    private static string BuildTempPath(string directory, string documentId, string runToken, string extension)
    {
        return Path.Combine(directory, documentId + "-" + runToken + extension);
    }

    private static string GenerateRunToken()
    {
        return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
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

    // SECTION: Log helpers
    private static void MirrorLogToLatest(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
        }
        catch
        {
            // Log mirroring is best-effort; ignore failures to avoid masking OCR results.
        }
    }
}
