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
                "Tick the box to confirm you looked at what will change.";
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
        MasterDataIntegrityStatus.Healthy => "OK",
        MasterDataIntegrityStatus.Warning => "Worth a look",
        MasterDataIntegrityStatus.Critical => "Needs fixing",
        _ => "Unknown"
    };

    private AdminPageHeaderModel BuildHeader() => new()
    {
        Title = "Data checks",
        Description = "Checks the lists used in forms for problems: values out of order, duplicate names, broken parent links and holiday mistakes. Each check says what it looks for.",
        Icon = "bi-clipboard2-check",
        Actions = new[]
        {
            new AdminPageActionModel
            {
                Text = "Lists used in forms",
                Href = Url.Page("/MasterData/Index", new { area = "Admin" }),
                Icon = "bi-arrow-left"
            }
        }
    };

    private static MasterDataIntegritySnapshot EmptySnapshot() =>
        new(Array.Empty<MasterDataIntegrityCheck>(), 0, 0, 0, 0, DateTimeOffset.MinValue);
}
