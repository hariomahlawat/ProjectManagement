using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.DocRepo
{
    public sealed class OcrmypdfDocumentOcrRunner : IDocumentOcrRunner
    {
        private readonly IDocStorage _storage;
        private readonly string _rootDir;
        private readonly string _inputDir;
        private readonly string _outputDir;
        private readonly string _logsDir;

        public OcrmypdfDocumentOcrRunner(
            IDocStorage storage,
            IOptions<DocRepoOptions> options,
            IUploadRootProvider uploadRootProvider)
        {
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(uploadRootProvider);

            _storage = storage;
            var value = options.Value ?? throw new ArgumentException("DocRepo options cannot be null.", nameof(options));

            _rootDir = EnsureDirectory(ResolveRoot(value, uploadRootProvider));
            _inputDir = EnsureDirectory(Path.Combine(_rootDir, ResolveSubpath(value.OcrInput, "input")));
            _outputDir = EnsureDirectory(Path.Combine(_rootDir, ResolveSubpath(value.OcrOutput, "output")));
            _logsDir = EnsureDirectory(Path.Combine(_rootDir, ResolveSubpath(value.OcrLogs, "logs")));
        }

        public async Task<OcrRunResult> RunAsync(Document document, CancellationToken ct = default)
        {
            var docId = document.Id.ToString();
            var runToken = GenerateRunToken();
            var inputPdf = BuildTempPath(_inputDir, docId, runToken, ".pdf");
            var outputPdf = BuildTempPath(_outputDir, docId, runToken, ".pdf");
            var sidecar = BuildTempPath(_outputDir, docId, runToken, ".txt");
            var logFile = BuildTempPath(_logsDir, docId, runToken, ".log");
            var latestLog = Path.Combine(_logsDir, docId + ".log");

            try
            {
                // SECTION: Write source PDF to worker input
                await using (var src = await _storage.OpenReadAsync(document.StoragePath, ct))
                await using (var dst = File.Create(inputPdf))
                {
                    await src.CopyToAsync(dst, ct);
                }

                // SECTION: First OCR pass
                var first = await RunOcrmypdfAsync(
                    args: $"--sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
                    workingDir: _rootDir,
                    ct: ct);

                // SECTION: Log first attempt
                await File.WriteAllTextAsync(
                    logFile,
                    $"FIRST RUN exit={first.ExitCode}{Environment.NewLine}{first.Stdout}{Environment.NewLine}{first.Stderr}",
                    ct);
                MirrorLogToLatest(logFile, latestLog);

                var isTaggedPdf = IsTaggedPdf(first);
                if (isTaggedPdf)
                {
                    // SECTION: Second OCR pass (skip-text)
                    var skipText = await RunOcrmypdfAsync(
                        args: $"--skip-text --sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
                        workingDir: _rootDir,
                        ct: ct);

                    // SECTION: Append log for skip-text run
                    await File.AppendAllTextAsync(
                        logFile,
                        $"{Environment.NewLine}SECOND RUN (skip-text) exit={skipText.ExitCode}{Environment.NewLine}{skipText.Stdout}{Environment.NewLine}{skipText.Stderr}",
                        ct);
                    MirrorLogToLatest(logFile, latestLog);

                    if (!File.Exists(sidecar))
                    {
                        return OcrRunResult.Fail($"ocrmypdf (skip-text) did not produce a sidecar file. See {logFile}");
                    }

                    var skipTextContent = await File.ReadAllTextAsync(sidecar, ct);
                    if (HasUsefulText(skipTextContent))
                    {
                        return OcrRunResult.Ok(skipTextContent);
                    }

                    // SECTION: Skip-text fallback (force OCR)
                    var forcedAfterSkip = await RunOcrmypdfAsync(
                        args: $"--force-ocr --sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
                        workingDir: _rootDir,
                        ct: ct);

                    await File.AppendAllTextAsync(
                        logFile,
                        $"{Environment.NewLine}THIRD RUN (force after skip-text) exit={forcedAfterSkip.ExitCode}{Environment.NewLine}{forcedAfterSkip.Stdout}{Environment.NewLine}{forcedAfterSkip.Stderr}",
                        ct);
                    MirrorLogToLatest(logFile, latestLog);

                    if (!File.Exists(sidecar))
                    {
                        return OcrRunResult.Fail($"ocrmypdf (force after skip-text) did not produce a sidecar file. See {logFile}");
                    }

                    var forcedAfterSkipContent = await File.ReadAllTextAsync(sidecar, ct);
                    if (!HasUsefulText(forcedAfterSkipContent))
                    {
                        return OcrRunResult.Fail($"ocrmypdf (force after skip-text) produced unusable text. See {logFile}");
                    }

                    return OcrRunResult.Ok(forcedAfterSkipContent);
                }

                var needForce =
                    first.ExitCode == 6 ||                             // PriorOcrFoundError
                    (first.Stderr?.IndexOf("PriorOcrFoundError", StringComparison.OrdinalIgnoreCase) >= 0);

                if (needForce)
                {
                    // SECTION: Second OCR pass (force)
                    var second = await RunOcrmypdfAsync(
                        args: $"--force-ocr --sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
                        workingDir: _rootDir,
                        ct: ct);

                    // SECTION: Append log for forced run
                    await File.AppendAllTextAsync(
                        logFile,
                        $"{Environment.NewLine}SECOND RUN (force) exit={second.ExitCode}{Environment.NewLine}{second.Stdout}{Environment.NewLine}{second.Stderr}",
                        ct);
                    MirrorLogToLatest(logFile, latestLog);

                    // after force run, decide
                    if (!File.Exists(sidecar))
                    {
                        return OcrRunResult.Fail($"ocrmypdf (forced) did not produce a sidecar file. See {logFile}");
                    }

                    var text2 = await File.ReadAllTextAsync(sidecar, ct);
                    return OcrRunResult.Ok(text2);
                }

                // SECTION: No force needed, read first pass result
                if (!File.Exists(sidecar))
                {
                    return OcrRunResult.Fail($"ocrmypdf did not produce a sidecar file. Exit {first.ExitCode}. See {logFile}");
                }

                var text = await File.ReadAllTextAsync(sidecar, ct);
                return OcrRunResult.Ok(text);
            }
            finally
            {
                // SECTION: Cleanup temporary artifacts
                TryDelete(inputPdf);
                TryDelete(outputPdf);
                TryDelete(sidecar);
            }
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunOcrmypdfAsync(
            string args,
            string workingDir,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ocrmypdf",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ocrmypdf process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return (process.ExitCode, stdout, stderr);
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

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ResolveRoot(DocRepoOptions options, IUploadRootProvider uploadRootProvider)
        {
            var configured = options.OcrWorkRoot;

            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = Path.Combine(uploadRootProvider.RootPath, "ocr-work");
            }
            else if (!Path.IsPathRooted(configured))
            {
                configured = Path.Combine(uploadRootProvider.RootPath, configured);
            }

            return Path.GetFullPath(configured);
        }

        private static string ResolveSubpath(string? configured, string fallback)
        {
            var trimmed = configured?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
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
}
