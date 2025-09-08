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
    }
}

