using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ocr;

// SECTION: PDF text extraction contract
public interface IPdfTextExtractor
{
    Task<string?> TryExtractAsync(string pdfPath, CancellationToken cancellationToken = default);
}
