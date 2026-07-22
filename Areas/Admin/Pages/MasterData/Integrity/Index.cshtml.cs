using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;

namespace ProjectManagement.Areas.Admin.Pages.MasterData.Integrity;

[Authorize(Policy = AdminPolicies.IntegrityManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminMasterDataIntegrityService _integrity;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IAdminMasterDataIntegrityService integrity,
        IAdminTimeService time)
    {
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public MasterDataIntegritySnapshot Snapshot { get; private set; } = EmptySnapshot();
    public AdminPageHeaderModel Header { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _integrity.InspectAsync(cancellationToken);
        Header = BuildHeader();
    }

    public async Task<IActionResult> OnPostNormaliseOrderAsync(
        string checkKey,
        bool acknowledge,
        CancellationToken cancellationToken)
    {
        if (!acknowledge)
        {
            TempData[FlashMessageKeys.AdminMasterDataError] =
                "Confirm that you reviewed the integrity findings before applying the repair.";
            return RedirectToPage(new { focus = checkKey });
        }

        var result = await _integrity.NormaliseOrderAsync(checkKey, cancellationToken);
        TempData[result.Succeeded
            ? FlashMessageKeys.AdminMasterDataSuccess
            : FlashMessageKeys.AdminMasterDataError] = result.UserMessage;
        return RedirectToPage(new { focus = checkKey });
    }

    public string FormatIst(DateTimeOffset value) => _time.FormatIst(value);

    public string Tone(MasterDataIntegrityStatus status) => status switch
    {
        MasterDataIntegrityStatus.Healthy => "success",
        MasterDataIntegrityStatus.Warning => "warning",
        MasterDataIntegrityStatus.Critical => "danger",
        _ => "neutral"
    };

    public string StatusLabel(MasterDataIntegrityStatus status) => status switch
    {
        MasterDataIntegrityStatus.Healthy => "Healthy",
        MasterDataIntegrityStatus.Warning => "Review",
        MasterDataIntegrityStatus.Critical => "Critical",
        _ => "Unknown"
    };

    private AdminPageHeaderModel BuildHeader() => new()
    {
        Eyebrow = "Master data · assurance",
        Title = "Configuration integrity",
        Description = "Assess hierarchy, naming, reference and display-order integrity before controlled corrective action.",
        Icon = "bi-clipboard2-check",
        Actions = new[]
        {
            new AdminPageActionModel
            {
                Text = "Integrity guidance",
                Href = (Url.Page("/Help/Index", new { area = "Admin" }) ?? "/Admin/Help") + "#configuration-integrity",
                Icon = "bi-question-circle"
            },
            new AdminPageActionModel
            {
                Text = "Master data centre",
                Href = Url.Page("/MasterData/Index", new { area = "Admin" }),
                Icon = "bi-arrow-left"
            }
        }
    };

    private static MasterDataIntegritySnapshot EmptySnapshot() =>
        new(Array.Empty<MasterDataIntegrityCheck>(), 0, 0, 0, 0, DateTimeOffset.MinValue);
}
