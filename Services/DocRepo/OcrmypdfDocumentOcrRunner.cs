using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Ocr;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.DocRepo
{
    // SECTION: DocRepo OCR runner using shared pipeline
    public sealed class OcrmypdfDocumentOcrRunner : IDocumentOcrRunner
    {
        private readonly IDocStorage _storage;
        private readonly string _ocrExecutable;
        private readonly string _rootDir;
        private readonly string _inputDir;
        private readonly string _outputDir;
        private readonly string _logsDir;
        private readonly OcrmypdfSharedRunner _sharedRunner;

        public OcrmypdfDocumentOcrRunner(
            IDocStorage storage,
            IOptions<DocRepoOptions> options,
            IUploadRootProvider uploadRootProvider,
            OcrmypdfSharedRunner sharedRunner)
        {
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(uploadRootProvider);
            ArgumentNullException.ThrowIfNull(sharedRunner);

            _storage = storage;
            _sharedRunner = sharedRunner;
            var value = options.Value ?? throw new ArgumentException("DocRepo options cannot be null.", nameof(options));

            _ocrExecutable = ResolveExecutablePath(value.OcrExecutablePath);
            _rootDir = EnsureDirectory(ResolveRoot(value, uploadRootProvider));
            _inputDir = EnsureDirectory(Path.Combine(_rootDir, ResolveSubpath(value.OcrInput, "input")));
            _outputDir = EnsureDirectory(Path.Combine(_rootDir, ResolveSubpath(value.OcrOutput, "output")));
            _logsDir = EnsureDirectory(Path.Combine(_rootDir, ResolveSubpath(value.OcrLogs, "logs")));
        }

        public async Task<OcrRunResult> RunAsync(Document document, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(document);

            var docId = document.Id.ToString();
            var sourcePdf = Path.Combine(_inputDir, $"{docId}-{Guid.NewGuid():N}-source.pdf");

            try
            {
                // SECTION: Write source PDF to worker input
                await using (var src = await _storage.OpenReadAsync(document.StoragePath, ct))
                await using (var dst = File.Create(sourcePdf))
                {
                    await src.CopyToAsync(dst, ct);
                }

                // SECTION: Execute shared OCR pipeline
                var request = new OcrmypdfSharedRequest
                {
                    DocumentId = docId,
                    OcrExecutable = _ocrExecutable,
                    WorkRoot = _rootDir,
                    InputDirectory = _inputDir,
                    OutputDirectory = _outputDir,
                    LogsDirectory = _logsDir,
                    SourcePdfPath = sourcePdf,
                    SourceAlreadyInWorkDirectory = true
                };

                var result = await _sharedRunner.RunAsync(request, ct);
                return result.Success
                    ? OcrRunResult.Ok(result.Text!)
                    : OcrRunResult.Fail(result.Error ?? "OCR failed.");
            }
            finally
            {
                // SECTION: Cleanup temporary artifacts
                TryDelete(sourcePdf);
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

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ResolveRoot(DocRepoOptions options, IUploadRootProvider uploadRootProvider)
        {
            var configured = ExpandPath(options.OcrWorkRoot);

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

        private static string ExpandPath(string? path)
        {
            var configured = (path ?? string.Empty).Trim();
            var expanded = Environment.ExpandEnvironmentVariables(configured);

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
}
