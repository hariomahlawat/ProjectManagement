using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.Celebrations;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authorization;
    private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

    public IndexModel(
        ApplicationDbContext db,
        IAuthorizationService authorization)
    {
        _db = db;
        _authorization = authorization;
    }

    public record Row(
        Guid Id,
        CelebrationType EventType,
        string Name,
        DateOnly NextOccurrence,
        int DaysAway);

    public Row[] Items { get; private set; } = Array.Empty<Row>();

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "all"; // all|birthday|anniversary

    [BindProperty(SupportsGet = true)]
    public string Window { get; set; } = "all"; // today|7|15|30|all

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public bool CanManageBirthdays { get; private set; }
    public bool CanManageAnniversaries { get; private set; }
    public bool CanEdit => CanManageBirthdays || CanManageAnniversaries;
    public string AddButtonLabel => CanManageAnniversaries ? "Add celebration" : "Add birthday";

    public bool CanManage(CelebrationType eventType) => eventType switch
    {
        CelebrationType.Birthday => CanManageBirthdays,
        CelebrationType.Anniversary => CanManageAnniversaries,
        _ => false
    };

    public async Task OnGetAsync()
    {
        await LoadPermissionsAsync();

        var query = _db.Celebrations
            .AsNoTracking()
            .Where(x => x.DeletedUtc == null);

        if (string.Equals(Type, "birthday", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.EventType == CelebrationType.Birthday);
        }
        else if (string.Equals(Type, "anniversary", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.EventType == CelebrationType.Anniversary);
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var search = Q.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, $"%{search}%") ||
                (x.SpouseName != null && EF.Functions.ILike(x.SpouseName, $"%{search}%")));
        }

        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var today = DateOnly.FromDateTime(nowLocal.DateTime);
        var celebrations = await query.ToListAsync();

        var rows = celebrations
            .Select(c =>
            {
                var next = CelebrationHelpers.NextOccurrenceLocal(c, today);
                return new Row(
                    c.Id,
                    c.EventType,
                    CelebrationHelpers.DisplayName(c),
                    next,
                    CelebrationHelpers.DaysAway(today, next));
            });

        rows = Window.ToLowerInvariant() switch
        {
            "today" => rows.Where(r => r.DaysAway == 0),
            "7" => rows.Where(r => r.DaysAway < 7),
            "15" => rows.Where(r => r.DaysAway < 15),
            "30" => rows.Where(r => r.DaysAway < 30),
            _ => rows
        };

        Items = rows
            .OrderBy(r => r.NextOccurrence)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var celebration = await _db.Celebrations
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null);

        if (celebration is null)
        {
            return RedirectToPage(new { Type, Window, Q });
        }

        var authorization = await _authorization.AuthorizeAsync(
            User,
            Policies.Calendar.PolicyFor(celebration.EventType));

        if (!authorization.Succeeded)
        {
            return Forbid();
        }

        celebration.DeletedUtc = DateTimeOffset.UtcNow;
        celebration.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        TempData["ok"] = celebration.EventType == CelebrationType.Birthday
            ? "Birthday deleted."
            : "Anniversary deleted.";

        return RedirectToPage(new { Type, Window, Q });
    }

    private async Task LoadPermissionsAsync()
    {
        CanManageBirthdays = (await _authorization.AuthorizeAsync(
            User,
            Policies.Calendar.ManageBirthdays)).Succeeded;

        CanManageAnniversaries = (await _authorization.AuthorizeAsync(
            User,
            Policies.Calendar.ManageAnniversaries)).Succeeded;
    }
}
