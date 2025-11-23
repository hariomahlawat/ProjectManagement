using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ocr;

// SECTION: Shared ocrmypdf runner request
public sealed class OcrmypdfSharedRequest
{
    public required string DocumentId { get; init; }
    public required string OcrExecutable { get; init; }
    public required string WorkRoot { get; init; }
    public required string InputDirectory { get; init; }
    public required string OutputDirectory { get; init; }
    public required string LogsDirectory { get; init; }
    public required string SourcePdfPath { get; init; }
    public bool SourceAlreadyInWorkDirectory { get; init; }
}

// SECTION: Shared ocrmypdf runner result
public sealed class OcrmypdfSharedResult
{
    public bool Success { get; init; }
    public string? Text { get; init; }
    public string? Error { get; init; }
    public string? LogFile { get; init; }

    public static OcrmypdfSharedResult Ok(string text, string logFile) => new()
    {
        Success = true,
        Text = text,
        LogFile = logFile
    };

    public static OcrmypdfSharedResult Fail(string error, string logFile) => new()
    {
        Success = false,
        Error = error,
        LogFile = logFile
    };
}

// SECTION: ocrmypdf invocation abstraction
public interface IOcrmypdfInvoker
{
    Task<OcrmypdfProcessResult> RunAsync(string executable, string args, string workingDirectory, CancellationToken cancellationToken);
}

// SECTION: ocrmypdf process result
public sealed record OcrmypdfProcessResult(int ExitCode, string Stdout, string Stderr);

// SECTION: Shared runner implementation
public sealed class OcrmypdfSharedRunner
{
    private readonly IOcrmypdfInvoker _invoker;
    private readonly IPdfTextExtractor _pdfTextExtractor;

    public OcrmypdfSharedRunner(IOcrmypdfInvoker invoker, IPdfTextExtractor pdfTextExtractor)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _pdfTextExtractor = pdfTextExtractor ?? throw new ArgumentNullException(nameof(pdfTextExtractor));
    }

    public async Task<OcrmypdfSharedResult> RunAsync(OcrmypdfSharedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runToken = GenerateRunToken();
        var inputPdf = request.SourceAlreadyInWorkDirectory
            ? request.SourcePdfPath
            : BuildTempPath(request.InputDirectory, request.DocumentId, runToken, ".pdf");
        var outputPdf = BuildTempPath(request.OutputDirectory, request.DocumentId, runToken, ".pdf");
        var sidecar = BuildTempPath(request.OutputDirectory, request.DocumentId, runToken, ".txt");
        var logFile = BuildTempPath(request.LogsDirectory, request.DocumentId, runToken, ".log");
        var latestLog = Path.Combine(request.LogsDirectory, request.DocumentId + ".log");

        try
        {
            if (!request.SourceAlreadyInWorkDirectory)
            {
                // SECTION: Copy source PDF into work area
                await using (var source = File.OpenRead(request.SourcePdfPath))
                await using (var destination = File.Create(inputPdf))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }
            }

            // SECTION: Embedded text fast path
            var embedded = await _pdfTextExtractor.TryExtractAsync(inputPdf, cancellationToken);
            if (OcrTextUtilities.HasUsefulText(embedded))
            {
                var cleaned = OcrTextUtilities.CleanBanners(embedded!);
                await File.WriteAllTextAsync(logFile, "Embedded text extracted; OCR skipped.", cancellationToken);
                MirrorLogToLatest(logFile, latestLog);
                return OcrmypdfSharedResult.Ok(cleaned, logFile);
            }

            // SECTION: First OCR pass (skip-text)
            var skipArgs = string.Join(' ', "--skip-text", "--sidecar", Quote(sidecar), Quote(inputPdf), Quote(outputPdf));
            await RunAndLogAsync(logFile, latestLog, label: "FIRST RUN (skip-text)", executable: request.OcrExecutable, args: skipArgs, workRoot: request.WorkRoot, append: false, cancellationToken);

            var skipTextContent = await ReadSidecarAsync(sidecar, cancellationToken);
            if (skipTextContent is null)
            {
                return OcrmypdfSharedResult.Fail($"ocrmypdf (skip-text) did not produce a sidecar file. See {logFile}", logFile);
            }

            if (OcrTextUtilities.HasUsefulText(skipTextContent))
            {
                return OcrmypdfSharedResult.Ok(OcrTextUtilities.CleanBanners(skipTextContent!), logFile);
            }

            // SECTION: Forced OCR when skip-text did not provide text
            var forceArgs = string.Join(' ', "--force-ocr", "--sidecar", Quote(sidecar), Quote(inputPdf), Quote(outputPdf));
            await RunAndLogAsync(logFile, latestLog, label: "SECOND RUN (force-ocr)", executable: request.OcrExecutable, args: forceArgs, workRoot: request.WorkRoot, append: true, cancellationToken);

            var forcedContent = await ReadSidecarAsync(sidecar, cancellationToken);
            if (forcedContent is null)
            {
                return OcrmypdfSharedResult.Fail($"ocrmypdf (force-ocr) did not produce a sidecar file. See {logFile}", logFile);
            }

            if (OcrTextUtilities.HasUsefulText(forcedContent))
            {
                return OcrmypdfSharedResult.Ok(OcrTextUtilities.CleanBanners(forcedContent!), logFile);
            }

            // SECTION: redo-ocr fallback for stubborn PDFs
            var redoArgs = string.Join(' ', "--redo-ocr", "--sidecar", Quote(sidecar), Quote(inputPdf), Quote(outputPdf));
            await RunAndLogAsync(logFile, latestLog, label: "THIRD RUN (redo-ocr)", executable: request.OcrExecutable, args: redoArgs, workRoot: request.WorkRoot, append: true, cancellationToken);

            var redoContent = await ReadSidecarAsync(sidecar, cancellationToken);
            if (redoContent is null)
            {
                return OcrmypdfSharedResult.Fail($"ocrmypdf (redo-ocr) did not produce a sidecar file. See {logFile}", logFile);
            }

            if (OcrTextUtilities.HasUsefulText(redoContent))
            {
                return OcrmypdfSharedResult.Ok(OcrTextUtilities.CleanBanners(redoContent!), logFile);
            }

            return OcrmypdfSharedResult.Fail($"ocrmypdf produced unusable text. See {logFile}", logFile);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(logFile, ex.ToString(), cancellationToken);
            MirrorLogToLatest(logFile, latestLog);
            return OcrmypdfSharedResult.Fail($"OCR failed: {ex.Message}. See {logFile}", logFile);
        }
        finally
        {
            // SECTION: Cleanup temporary artifacts
            if (!request.SourceAlreadyInWorkDirectory)
            {
                TryDelete(inputPdf);
            }

            TryDelete(outputPdf);
            TryDelete(sidecar);
        }
    }

    // SECTION: Process execution helpers
    private async Task<OcrmypdfProcessResult> RunAndLogAsync(
        string logFile,
        string latestLog,
        string label,
        string executable,
        string args,
        string workRoot,
        bool append,
        CancellationToken cancellationToken)
    {
        var result = await _invoker.RunAsync(executable, args, workRoot, cancellationToken);
        var content = $"{(append ? Environment.NewLine : string.Empty)}{label} exit={result.ExitCode}{Environment.NewLine}{result.Stdout}{Environment.NewLine}{result.Stderr}";

        if (append)
        {
            await File.AppendAllTextAsync(logFile, content, cancellationToken);
        }
        else
        {
            await File.WriteAllTextAsync(logFile, content, cancellationToken);
        }

        MirrorLogToLatest(logFile, latestLog);
        return result;
    }

    private static async Task<string?> ReadSidecarAsync(string sidecar, CancellationToken cancellationToken)
    {
        if (!File.Exists(sidecar))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(sidecar, cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    private static string Quote(string path)
    {
        return string.Concat('"', path, '"');
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
