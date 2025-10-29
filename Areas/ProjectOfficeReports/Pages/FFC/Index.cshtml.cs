using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class IndexModel : FfcRecordListPageModel
{
    public IndexModel(ApplicationDbContext db)
        : base(db)
    {
    }

    public bool CanManageRecords { get; private set; }

    public async Task OnGetAsync()
    {
        CanManageRecords = User.IsInRole("Admin") || User.IsInRole("HoD");
        await LoadRecordsAsync();
    }
}
