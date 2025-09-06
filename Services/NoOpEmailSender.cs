using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Tasks;

namespace ProjectManagement.Services
{
    public class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // Email sending disabled for private LAN; no operation performed.
            return Task.CompletedTask;
        }
    }
}
