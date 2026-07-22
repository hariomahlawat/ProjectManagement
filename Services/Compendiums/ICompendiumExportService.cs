using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Compendiums;

public interface ICompendiumExportService
{
    Task<CompendiumExportResult> GenerateAsync(
        CompendiumExportRequest request,
        CancellationToken cancellationToken = default);

    Task<CompendiumExportResult> GenerateAsync(CancellationToken cancellationToken = default);
}

public sealed record CompendiumExportRequest(string? HandlingMarking = null);

public sealed record CompendiumExportResult(byte[] Bytes, string FileName);
