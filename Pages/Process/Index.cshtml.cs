using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Pages.Process;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public string ProcessVersion { get; private set; } = PlanConstants.StageTemplateVersion;
    public bool CanEditChecklist { get; private set; }
        = false;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var latestVersion = await _db.StageTemplates
            .AsNoTracking()
            .OrderByDescending(t => t.Version)
            .Select(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(latestVersion))
        {
            ProcessVersion = latestVersion;
        }

        CanEditChecklist = User.IsInRole("MCO") || User.IsInRole("HoD");
    }
}
