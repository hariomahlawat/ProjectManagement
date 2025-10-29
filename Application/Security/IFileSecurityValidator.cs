using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Application.Security;

public interface IFileSecurityValidator
{
    Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default);
}
