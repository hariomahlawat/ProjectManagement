using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.DocRepo;

public sealed class DocRepoCoverServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prism-covers-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingDocStorage _storage = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Covers_TurnedOff_ReturnNothing()
    {
        var document = AddDocument(OnePagePdf());
        var service = CreateService(enabled: false);

        Assert.Null(await service.GetCoverAsync(document, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_root, "covers")));
    }

    [Fact]
    public async Task MissingPdfToPpm_FallsBack_WithoutMarkingTheDocument()
    {
        var document = AddDocument(OnePagePdf());
        var service = CreateService(executable: Path.Combine(_root, "no-such-pdftoppm"));

        Assert.Null(await service.GetCoverAsync(document, CancellationToken.None));
        // Installing Poppler later must give covers without any clean-up.
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "covers"), "*.failed", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ValidPdf_GetsAJpegCover_ThatIsReused_AndDeleted()
    {
        if (!PdfToPpmInstalled()) return; // Poppler is not on this machine; covered where it is.

        var document = AddDocument(OnePagePdf());
        var service = CreateService();

        var path = await service.GetCoverAsync(document, CancellationToken.None);

        Assert.NotNull(path);
        var bytes = await File.ReadAllBytesAsync(path!);
        Assert.True(bytes.Length > 100);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(path, await service.GetCoverAsync(document, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path!)!, "*.pdf"));

        service.DeleteCover(document.Id);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DamagedPdf_IsMarked_AndNotDrawnAgainStraightAway()
    {
        if (!PdfToPpmInstalled()) return;

        var document = AddDocument(Encoding.ASCII.GetBytes("not a pdf at all"));
        var service = CreateService();

        Assert.Null(await service.GetCoverAsync(document, CancellationToken.None));
        var marker = Assert.Single(Directory.GetFiles(Path.Combine(_root, "covers"), "*.failed", SearchOption.AllDirectories));

        // The marker answers the next request without running pdftoppm again.
        _storage.Put(document.StoragePath, OnePagePdf());
        Assert.Null(await service.GetCoverAsync(document, CancellationToken.None));

        service.DeleteCover(document.Id);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ConcurrentRequests_ShareOneDrawing()
    {
        if (!PdfToPpmInstalled()) return;

        var document = AddDocument(OnePagePdf());
        var service = CreateService();

        var results = await Task.WhenAll(
            service.GetCoverAsync(document, CancellationToken.None),
            service.GetCoverAsync(document, CancellationToken.None),
            service.GetCoverAsync(document, CancellationToken.None));

        Assert.All(results, Assert.NotNull);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "covers"), "*.jpg", SearchOption.AllDirectories));
    }

    private Document AddDocument(byte[] pdf)
    {
        var document = new Document { Id = Guid.NewGuid(), StoragePath = $"2026/10/{Guid.NewGuid():N}.pdf", Subject = "Cover test" };
        _storage.Put(document.StoragePath, pdf);
        return document;
    }

    private DocRepoCoverService CreateService(bool enabled = true, string? executable = null)
        => new(
            Options.Create(new DocRepoOptions { RootPath = _root, EnableCovers = enabled, PdfToPpmPath = executable }),
            new TestWebHostEnvironment { ContentRootPath = _root },
            _storage,
            NullLogger<DocRepoCoverService>.Instance);

    private static bool PdfToPpmInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pdftoppm", "-v") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false });
            process!.WaitForExit(5000);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // A real one-page PDF (a filled rectangle), with a correct cross-reference table.
    internal static byte[] OnePagePdf()
    {
        var content = "0.2 0.4 0.8 rg 72 72 451 698 re f";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = builder.Length;
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
