using Microsoft.AspNetCore.Identity;

namespace ProjectManagement.Models
{
    public class ApplicationUser : IdentityUser
    {
        public bool MustChangePassword { get; set; } = true;

        public string FullName { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;

        public DateTime? LastLoginUtc { get; set; }
        public int LoginCount { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public bool IsDisabled { get; set; }
        public DateTime? DisabledUtc { get; set; }
        public string? DisabledByUserId { get; set; }

        public bool PendingDeletion { get; set; }
        public DateTime? DeletionRequestedUtc { get; set; }
        public string? DeletionRequestedByUserId { get; set; }

        public bool ShowCelebrationsInCalendar { get; set; } = true;
    }
}

