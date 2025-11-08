using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

// SECTION: ocrmypdf-based OCR runner implementation
public sealed class OcrmypdfDocumentOcrRunner : IDocumentOcrRunner
{
    private readonly IDocStorage _storage;
    private readonly string _workDir;

    public OcrmypdfDocumentOcrRunner(IDocStorage storage)
    {
        _storage = storage;
        _workDir = @"C:\ocr-work";
    }

    public async Task<OcrRunResult> RunAsync(Document document, CancellationToken ct = default)
    {
        // SECTION: Prepare working files
        var inputPdf = Path.Combine(_workDir, $"{document.Id}.pdf");
        var outputPdf = Path.Combine(_workDir, $"{document.Id}-out.pdf");
        var sidecar = Path.Combine(_workDir, $"{document.Id}.txt");

        Directory.CreateDirectory(_workDir);

        await using (var source = await _storage.OpenReadAsync(document.StoragePath, ct))
        await using (var destination = File.Create(inputPdf))
        {
            await source.CopyToAsync(destination, ct);
        }

        // SECTION: Execute ocrmypdf
        var processInfo = new ProcessStartInfo
        {
            FileName = "ocrmypdf",
            Arguments = $"--sidecar \"{sidecar}\" \"{inputPdf}\" \"{outputPdf}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process is null)
        {
            return OcrRunResult.Fail("Failed to start ocrmypdf process.");
        }

        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            return OcrRunResult.Fail(string.IsNullOrWhiteSpace(stderr)
                ? $"ocrmypdf exited with code {process.ExitCode}."
                : stderr);
        }

        // SECTION: Read extracted text
        if (!File.Exists(sidecar))
        {
            return OcrRunResult.Fail("ocrmypdf did not produce a sidecar file.");
        }

        var text = await File.ReadAllTextAsync(sidecar, ct);

        return OcrRunResult.Ok(text);
    }
}
