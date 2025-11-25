using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace ProjectManagement.Pages
{
    // ===========================
    // DEVELOPER PAGE MODEL
    // ===========================
    public class DeveloperModel : PageModel
    {
        private readonly IConfiguration _configuration;

        // ===========================
        // PROPERTIES
        // ===========================
        public string AppVersion { get; private set; } = "1.0";

        // ===========================
        // CONSTRUCTOR
        // ===========================
        public DeveloperModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ===========================
        // GET HANDLER
        // ===========================
        public void OnGet()
        {
            AppVersion = _configuration["App:Version"] ?? "1.0";
        }
    }
}
