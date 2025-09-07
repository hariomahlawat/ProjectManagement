using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Models
{
    public class AuditLog
    {
        public long Id { get; set; }

        [Required]
        public DateTime TimeUtc { get; set; } = IstClock.Now;

        [Required, StringLength(16)]
        public string Level { get; set; } = "Info"; // Info, Warning, Error

        [Required, StringLength(64)]
        public string Action { get; set; } = string.Empty; // e.g., LoginSuccess, AdminUserCreated

        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }

        [StringLength(1024)]
        public string? Message { get; set; }

        // optional extra payload (JSON)
        public string? DataJson { get; set; }
    }
}
