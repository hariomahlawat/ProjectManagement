using System;

namespace ProjectManagement.Models
{
    public class AuthEvent
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = default!;
        public DateTimeOffset WhenUtc { get; set; }
        public string Event { get; set; } = "LoginSucceeded";
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
    }
}
