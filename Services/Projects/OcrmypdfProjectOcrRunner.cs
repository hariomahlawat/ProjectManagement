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
    private readonly IProjectDocumentStorageResolver _storageResolver;
    private readonly ProjectDocumentOcrOptions _options;

    public OcrmypdfProjectOcrRunner(
        IProjectDocumentStorageResolver storageResolver,
        IOptions<ProjectDocumentOcrOptions> options)
    {
        _storageResolver = storageResolver ?? throw new ArgumentNullException(nameof(storageResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ProjectDocumentOcrResult> RunAsync(ProjectDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sourcePath = _storageResolver.ResolveAbsolutePath(document.StorageKey);
        if (!File.Exists(sourcePath))
        {
            return ProjectDocumentOcrResult.Failure($"Source file not found at '{sourcePath}'.");
        }

        var workRoot = ResolveWorkRoot();
        Directory.CreateDirectory(workRoot);

        var runToken = $"{document.Id}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var runDirectory = Path.Combine(workRoot, runToken);
        Directory.CreateDirectory(runDirectory);

        var sidecarPath = Path.Combine(runDirectory, "output.txt");
        var outputPath = Path.Combine(runDirectory, "output.pdf");
        var logsDirectory = Path.Combine(workRoot, "logs");
        Directory.CreateDirectory(logsDirectory);
        var logPath = Path.Combine(logsDirectory, $"{document.Id}.log");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ocrmypdf",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--sidecar");
            startInfo.ArgumentList.Add(sidecarPath);
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start ocrmypdf process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);
            await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0 && File.Exists(sidecarPath))
            {
                var text = await File.ReadAllTextAsync(sidecarPath, cancellationToken);
                return ProjectDocumentOcrResult.SuccessResult(text);
            }

            await File.WriteAllTextAsync(logPath, stderr ?? string.Empty, cancellationToken);
            return ProjectDocumentOcrResult.Failure($"ocrmypdf exited with code {process.ExitCode}. See {logPath} for details.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(logPath, ex.ToString(), cancellationToken);
            return ProjectDocumentOcrResult.Failure($"OCR failed: {ex.Message}. See {logPath} for details.");
        }
        finally
        {
            TryDeleteFile(sidecarPath);
            TryDeleteFile(outputPath);
            TryDeleteDirectory(runDirectory);
        }
    }

    private string ResolveWorkRoot()
    {
        var configured = _options.WorkRoot;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Path.Combine(AppContext.BaseDirectory, "project-ocr");
        }

        if (!Path.IsPathRooted(configured))
        {
            configured = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        return configured;
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
