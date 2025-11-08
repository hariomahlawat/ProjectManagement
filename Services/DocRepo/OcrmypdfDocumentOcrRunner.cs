using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore; // only if you need it elsewhere, remove if unused
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo
{
    public sealed class OcrmypdfDocumentOcrRunner : IDocumentOcrRunner
    {
        private readonly IDocStorage _storage;
        private readonly string _rootDir;
        private readonly string _inputDir;
        private readonly string _outputDir;
        private readonly string _logsDir;

        public OcrmypdfDocumentOcrRunner(IDocStorage storage)
        {
            _storage = storage;
            _rootDir = @"C:\ocr-work";
            _inputDir = Path.Combine(_rootDir, "input");
            _outputDir = Path.Combine(_rootDir, "output");
            _logsDir = Path.Combine(_rootDir, "logs");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_inputDir);
            Directory.CreateDirectory(_outputDir);
            Directory.CreateDirectory(_logsDir);
        }

        public async Task<OcrRunResult> RunAsync(Document document, CancellationToken ct = default)
        {
            var docId = document.Id.ToString();
            var inputPdf = Path.Combine(_inputDir, docId + ".pdf");
            var outputPdf = Path.Combine(_outputDir, docId + ".pdf");
            var sidecar = Path.Combine(_outputDir, docId + ".txt");
            var logFile = Path.Combine(_logsDir, docId + ".log");

            // write source pdf
            await using (var src = await _storage.OpenReadAsync(document.StoragePath, ct))
            await using (var dst = File.Create(inputPdf))
            {
                await src.CopyToAsync(dst, ct);
            }

            // 1st run: normal
            var first = await RunOcrmypdfAsync(
                args: $"--sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
                workingDir: _rootDir,
                ct: ct);

            // log first run
            await File.WriteAllTextAsync(
                logFile,
                $"FIRST RUN exit={first.ExitCode}{Environment.NewLine}{first.Stdout}{Environment.NewLine}{first.Stderr}",
                ct);

            var needForce =
                first.ExitCode == 6 ||                             // PriorOcrFoundError
                (first.Stderr?.IndexOf("PriorOcrFoundError", StringComparison.OrdinalIgnoreCase) >= 0);

            if (needForce)
            {
                // 2nd run: force-ocr
                var second = await RunOcrmypdfAsync(
                    args: $"--force-ocr --sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
                    workingDir: _rootDir,
                    ct: ct);

                // append to log
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

            // no force needed, just check result of first run
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
    }
}
