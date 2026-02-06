using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Application.Security;

public interface IFileSecurityValidator
{
    // Section: Relative path validation
    void ValidateRelativePath(string relativePath);

    // Section: File content validation
    Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default);
}
