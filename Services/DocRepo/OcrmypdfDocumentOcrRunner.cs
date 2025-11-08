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
            var inputPdf = Path.Combine(_inputDir, docId + ".pdf");
            var outputPdf = Path.Combine(_outputDir, docId + ".pdf");
            var sidecar = Path.Combine(_outputDir, docId + ".txt");
            var logFile = Path.Combine(_logsDir, docId + ".log");

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
    }
}
