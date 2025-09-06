using Microsoft.AspNetCore.Identity;

namespace ProjectManagement.Models
{
    public class ApplicationUser : IdentityUser
    {
        public bool MustChangePassword { get; set; } = true;
    }
}

