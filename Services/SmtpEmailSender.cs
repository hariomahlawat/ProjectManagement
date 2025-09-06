using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;

namespace ProjectManagement.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;

        public SmtpEmailSender(IConfiguration config)
        {
            _config = config;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var host = _config["Email:Smtp:Host"];
            var port = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 25;
            var username = _config["Email:Smtp:Username"];
            var password = _config["Email:Smtp:Password"];
            var from = _config["Email:From"];
            if (string.IsNullOrWhiteSpace(from))
            {
                from = username ?? "no-reply@example.com";
            }

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = true
            };

            var mailMessage = new MailMessage(from, email, subject, htmlMessage) { IsBodyHtml = true };
            return client.SendMailAsync(mailMessage);
        }
    }
}
