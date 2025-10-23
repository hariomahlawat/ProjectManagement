using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public interface IIprWriteService
{
    Task<IprRecord> CreateAsync(IprRecord record, CancellationToken cancellationToken = default);

    Task<IprRecord?> UpdateAsync(IprRecord record, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<IprAttachment> AddAttachmentAsync(
        int iprRecordId,
        Stream content,
        string originalFileName,
        string? contentType,
        string uploadedByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAttachmentAsync(int attachmentId, byte[] rowVersion, CancellationToken cancellationToken = default);
}
