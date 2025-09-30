using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services
{
    public interface IVirusScanner
    {
        Task ScanAsync(Stream content, string fileName, CancellationToken cancellationToken);
    }
}
