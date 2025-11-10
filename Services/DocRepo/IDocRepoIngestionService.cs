using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Ingestion service contract
public interface IDocRepoIngestionService
{
    Task<Guid> IngestExternalPdfAsync(
        Stream pdfStream,
        string originalFileName,
        string sourceModule,
        string sourceItemId,
        CancellationToken cancellationToken = default);
}
