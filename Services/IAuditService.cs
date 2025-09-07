using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Services
{
    public interface IAuditService
    {
        Task LogAsync(string action, string? message = null, string level = "Info",
                      string? userId = null, string? userName = null,
                      object? data = null, HttpContext? http = null);
    }
}
