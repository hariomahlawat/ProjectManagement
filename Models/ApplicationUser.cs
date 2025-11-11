using Microsoft.AspNetCore.Identity;

namespace ProjectManagement.Models
{
    public class ApplicationUser : IdentityUser
    {
        // SECTION: Security lifecycle
        public bool MustChangePassword { get; set; } = true;

        // SECTION: Profile
        public string FullName { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;

        // SECTION: Session activity
        public DateTime? LastLoginUtc { get; set; }
        public int LoginCount { get; set; }

        // SECTION: Audit timestamps
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        // SECTION: Account state
        public bool IsDisabled { get; set; }
        public DateTime? DisabledUtc { get; set; }
        public string? DisabledByUserId { get; set; }

        // SECTION: Deletion workflow
        public bool PendingDeletion { get; set; }
        public DateTime? DeletionRequestedUtc { get; set; }
        public string? DeletionRequestedByUserId { get; set; }

        // SECTION: Role preferences
        public string? DefaultUserRoleId { get; set; }

        // SECTION: Personalisation
        public bool ShowCelebrationsInCalendar { get; set; } = true;
    }
}

