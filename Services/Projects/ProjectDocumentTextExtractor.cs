using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Documents;
using DrawingText = DocumentFormat.OpenXml.Drawing.Text;
using WordprocessingText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace ProjectManagement.Services.Projects;

// SECTION: Project document text extraction implementation
public sealed class ProjectDocumentTextExtractor : IProjectDocumentTextExtractor
{
    // SECTION: Supported content types
    private static readonly HashSet<string> WordContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private static readonly HashSet<string> PowerPointContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    private static readonly HashSet<string> ExcelContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    // SECTION: Dependencies
    private readonly IProjectDocumentStorageResolver _storageResolver;
    private readonly ProjectDocumentTextExtractorOptions _options;

    // SECTION: Constructor
    public ProjectDocumentTextExtractor(
        IProjectDocumentStorageResolver storageResolver,
        IOptions<ProjectDocumentTextExtractorOptions> options)
    {
        _storageResolver = storageResolver ?? throw new ArgumentNullException(nameof(storageResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    // SECTION: Extraction entry point
    public async Task<ProjectDocumentTextExtractionResult> ExtractAsync(
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var contentType = document.ContentType ?? string.Empty;
        if (!IsSupportedContentType(contentType))
        {
            return ProjectDocumentTextExtractionResult.NotApplicable();
        }

        var sourcePath = _storageResolver.ResolveAbsolutePath(document.StorageKey);
        if (!File.Exists(sourcePath))
        {
            return ProjectDocumentTextExtractionResult.Failed($"Source file not found at '{sourcePath}'.");
        }

        try
        {
            var extractedText = ExtractText(sourcePath, contentType, cancellationToken);
            var conversionResult = await TryConvertToPdfAsync(document, sourcePath, contentType, cancellationToken)
                .ConfigureAwait(false);

            return ProjectDocumentTextExtractionResult.Extracted(
                extractedText,
                conversionResult.PdfDerivativeStorageKey,
                conversionResult.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProjectDocumentTextExtractionResult.Failed($"Text extraction failed: {ex.Message}");
        }
    }

    // SECTION: Conversion helpers
    private async Task<ProjectDocumentPdfConversionResult> TryConvertToPdfAsync(
        ProjectDocument document,
        string sourcePath,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (!_options.EnablePdfConversion || !IsSupportedContentType(contentType))
        {
            return ProjectDocumentPdfConversionResult.NotAttempted();
        }

        var derivativeStorageKey = BuildDerivativeStorageKey(document.StorageKey, document.Id);
        var targetPath = _storageResolver.ResolveAbsolutePath(derivativeStorageKey);
        var outputDirectory = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return ProjectDocumentPdfConversionResult.Failed("Unable to resolve output directory for PDF conversion.");
        }

        Directory.CreateDirectory(outputDirectory);

        TryDelete(targetPath);

        var executable = ResolveLibreOfficeExecutable(_options.LibreOfficeExecutablePath);

        var args = string.Join(' ', new[]
        {
            "--headless",
            "--nologo",
            "--nolockcheck",
            "--nodefault",
            "--nofirststartwizard",
            "--convert-to",
            "pdf",
            "--outdir",
            Quote(outputDirectory),
            Quote(sourcePath)
        });

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return ProjectDocumentPdfConversionResult.Failed("LibreOffice process failed to start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var message = !string.IsNullOrWhiteSpace(stderr)
                    ? stderr
                    : !string.IsNullOrWhiteSpace(stdout) ? stdout : "LibreOffice conversion failed.";
                return ProjectDocumentPdfConversionResult.Failed(message.Trim());
            }

            if (!File.Exists(targetPath))
            {
                return ProjectDocumentPdfConversionResult.Failed("LibreOffice conversion did not produce a PDF output.");
            }

            return ProjectDocumentPdfConversionResult.Succeeded(derivativeStorageKey);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProjectDocumentPdfConversionResult.Failed($"LibreOffice conversion failed: {ex.Message}");
        }
    }

    private static string ResolveLibreOfficeExecutable(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return "soffice";
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
        var fullPath = Path.GetFullPath(expanded);

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Configured LibreOffice executable was not found at '{fullPath}'.");
        }

        return fullPath;
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup; ignore failures.
        }
    }

    private string BuildDerivativeStorageKey(string storageKey, int documentId)
    {
        var normalized = NormalizeStorageKey(storageKey);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileName = segments.Length > 0 ? segments[^1] : $"document-{documentId}";
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"document-{documentId}";
        }

        var directory = segments.Length > 1 ? string.Join('/', segments[..^1]) : string.Empty;
        var prefix = _options.DerivativeStoragePrefix.Trim().Trim('/');

        return string.IsNullOrEmpty(directory)
            ? $"{prefix}/{baseName}.pdf"
            : $"{prefix}/{directory}/{baseName}.pdf";
    }

    private static string NormalizeStorageKey(string storageKey)
    {
        var normalized = storageKey.Replace('\\', '/').Trim();
        return normalized.TrimStart('/');
    }

    private static bool IsSupportedContentType(string contentType)
    {
        return WordContentTypes.Contains(contentType)
               || PowerPointContentTypes.Contains(contentType)
               || ExcelContentTypes.Contains(contentType);
    }

    // SECTION: Text extraction helpers
    private static string ExtractText(string sourcePath, string contentType, CancellationToken cancellationToken)
    {
        if (WordContentTypes.Contains(contentType))
        {
            return ExtractWordText(sourcePath, cancellationToken);
        }

        if (PowerPointContentTypes.Contains(contentType))
        {
            return ExtractPowerPointText(sourcePath, cancellationToken);
        }

        if (ExcelContentTypes.Contains(contentType))
        {
            return ExtractExcelText(sourcePath, cancellationToken);
        }

        return string.Empty;
    }

    private static string ExtractWordText(string sourcePath, CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(sourcePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Concat(paragraph.Descendants<WordprocessingText>().Select(text => text.Text));
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(line.Trim());
        }

        return builder.ToString();
    }

    private static string ExtractPowerPointText(string sourcePath, CancellationToken cancellationToken)
    {
        using var document = PresentationDocument.Open(sourcePath, false);
        var presentationPart = document.PresentationPart;
        if (presentationPart == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var slideParts = presentationPart.SlideParts.ToList();

        foreach (var slidePart in slideParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slide = slidePart.Slide;
            if (slide == null)
            {
                continue;
            }

            var textNodes = slide.Descendants<DrawingText>().Select(node => node.Text).Where(value => !string.IsNullOrWhiteSpace(value));
            var slideText = string.Join(' ', textNodes).Trim();
            if (string.IsNullOrWhiteSpace(slideText))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(slideText);
        }

        return builder.ToString();
    }

    private static string ExtractExcelText(string sourcePath, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(sourcePath);
        var builder = new StringBuilder();

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = worksheet.RowsUsed();

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = row.CellsUsed()
                    .Select(cell => cell.GetFormattedString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();

                if (values.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine(string.Join(' ', values));
            }
        }

        return builder.ToString();
    }

    // SECTION: PDF conversion result
    private sealed record ProjectDocumentPdfConversionResult
    {
        public bool Attempted { get; init; }

        public bool Success { get; init; }

        public string? PdfDerivativeStorageKey { get; init; }

        public string? Error { get; init; }

        public static ProjectDocumentPdfConversionResult NotAttempted() => new()
        {
            Attempted = false,
            Success = false
        };

        public static ProjectDocumentPdfConversionResult Succeeded(string storageKey) => new()
        {
            Attempted = true,
            Success = true,
            PdfDerivativeStorageKey = storageKey
        };

        public static ProjectDocumentPdfConversionResult Failed(string error) => new()
        {
            Attempted = true,
            Success = false,
            Error = error
        };
    }
}
