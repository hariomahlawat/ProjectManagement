using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages
{
    // ===========================
    // ABOUT PRISM (/Developer, "About PRISM" in the footer)
    // The running build, for users quoting it to the administrator, and the credit.
    // ===========================
    public class DeveloperModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public DeveloperModel(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        // ===========================
        // PROPERTIES
        // ===========================
        public string AppVersion { get; private set; } = "1.0";

        public string? Commit { get; private set; }

        public string? ShortCommit { get; private set; }

        public string BuiltOn { get; private set; } = "Not recorded";

        public string RunningSince { get; private set; } = string.Empty;

        public string Runtime { get; private set; } = string.Empty;

        public string EnvironmentName { get; private set; } = string.Empty;

        /// <summary>Plain text of the system details, for the "Copy details" button.</summary>
        public string DetailsText { get; private set; } = string.Empty;

        // ===========================
        // GET HANDLER
        // ===========================
        public void OnGet()
        {
            var build = AppBuildInfo.Current;
            var ist = TimeZoneHelper.GetIst();
            var culture = CultureInfo.GetCultureInfo("en-GB");

            // App:Version stays the override an operator can set; otherwise the assembly version.
            AppVersion = _configuration["App:Version"] is { Length: > 0 } configured ? configured : build.AssemblyVersion;
            Commit = build.Commit;
            ShortCommit = build.ShortCommit;
            if (build.BuiltUtc is { } built)
            {
                BuiltOn = TimeZoneInfo.ConvertTime(built, ist).ToString("d MMM yyyy, HH:mm", culture);
            }

            RunningSince = TimeZoneInfo.ConvertTime(build.StartedUtc, ist).ToString("d MMM yyyy, HH:mm", culture);
            Runtime = build.Runtime;
            EnvironmentName = _environment.EnvironmentName;

            DetailsText = string.Join(Environment.NewLine, new[]
            {
                $"PRISM ERP {AppVersion}",
                $"Build: {ShortCommit ?? "not recorded"}",
                build.BuiltUtc is null ? "Built: not recorded" : $"Built: {BuiltOn} IST",
                $"Running since: {RunningSince} IST",
                $"Runtime: {Runtime}",
                $"Environment: {EnvironmentName}",
            });
        }
    }
}
