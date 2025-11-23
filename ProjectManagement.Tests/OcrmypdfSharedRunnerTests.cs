using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services.Ocr;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class OcrmypdfSharedRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesEmbeddedTextFastPath()
    {
        var invoker = new RecordingInvoker();
        var extractor = new StubPdfTextExtractor("Embedded content");
        var runner = new OcrmypdfSharedRunner(invoker, extractor);

        var root = CreateTempRoot();
        var inputDir = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        var outputDir = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        var logsDir = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
        var sourcePdf = Path.Combine(inputDir, "source.pdf");
        await File.WriteAllTextAsync(sourcePdf, "pdf");

        try
        {
            var request = new OcrmypdfSharedRequest
            {
                DocumentId = "doc-1",
                OcrExecutable = "ocrmypdf",
                WorkRoot = root,
                InputDirectory = inputDir,
                OutputDirectory = outputDir,
                LogsDirectory = logsDir,
                SourcePdfPath = sourcePdf,
                SourceAlreadyInWorkDirectory = true
            };

            var result = await runner.RunAsync(request, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Embedded content", result.Text);
            Assert.Empty(invoker.Invocations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FallsBackToForceOcrWhenSkipTextHasNoContent()
    {
        var sidecarTexts = new Queue<string>(new[]
        {
            "[OCR skipped on page 1]",
            "Clean text from OCR"
        });

        var invoker = new RecordingInvoker(sidecarTexts);
        var extractor = new StubPdfTextExtractor(null);
        var runner = new OcrmypdfSharedRunner(invoker, extractor);

        var root = CreateTempRoot();
        var inputDir = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        var outputDir = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        var logsDir = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
        var sourcePdf = Path.Combine(inputDir, "source.pdf");
        await File.WriteAllTextAsync(sourcePdf, "pdf");

        try
        {
            var request = new OcrmypdfSharedRequest
            {
                DocumentId = "doc-2",
                OcrExecutable = "ocrmypdf",
                WorkRoot = root,
                InputDirectory = inputDir,
                OutputDirectory = outputDir,
                LogsDirectory = logsDir,
                SourcePdfPath = sourcePdf,
                SourceAlreadyInWorkDirectory = true
            };

            var result = await runner.RunAsync(request, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Clean text from OCR", result.Text);
            Assert.Equal(2, invoker.Invocations.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // SECTION: Test helpers
    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "ocr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubPdfTextExtractor : IPdfTextExtractor
    {
        private readonly string? _text;

        public StubPdfTextExtractor(string? text)
        {
            _text = text;
        }

        public Task<string?> TryExtractAsync(string pdfPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_text);
        }
    }

    private sealed class RecordingInvoker : IOcrmypdfInvoker
    {
        private readonly Queue<string> _sidecarTexts;

        public RecordingInvoker()
        {
            _sidecarTexts = new Queue<string>();
        }

        public RecordingInvoker(Queue<string> sidecarTexts)
        {
            _sidecarTexts = sidecarTexts;
        }

        public List<string> Invocations { get; } = new();

        public Task<OcrmypdfProcessResult> RunAsync(string executable, string args, string workingDirectory, CancellationToken cancellationToken)
        {
            Invocations.Add(args);

            var sidecar = ExtractSidecar(args);
            if (sidecar is not null && _sidecarTexts.Count > 0)
            {
                var text = _sidecarTexts.Dequeue();
                File.WriteAllText(sidecar, text);
            }

            return Task.FromResult(new OcrmypdfProcessResult(0, string.Empty, string.Empty));
        }

        private static string? ExtractSidecar(string args)
        {
            const string marker = "--sidecar \"";
            var start = args.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            var end = args.IndexOf('"', start);
            return end > start ? args[start..end] : null;
        }
    }
}
