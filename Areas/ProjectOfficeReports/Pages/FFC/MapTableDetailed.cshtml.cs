using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Ffc.Exports;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
[ValidateAntiForgeryToken]
public class MapTableDetailedModel : PageModel
{
    private const int OverallRemarksMaxLength = 4000;

    private readonly ApplicationDbContext _db;
    private readonly IFfcQueryService _ffcQueryService;
    private readonly IFfcProgressService _ffcProgressService;
    private readonly IFfcDetailedTableExportService _exportService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MapTableDetailedModel> _logger;

    // SECTION: Construction
    public MapTableDetailedModel(
        ApplicationDbContext db,
        IFfcQueryService ffcQueryService,
        IFfcProgressService ffcProgressService,
        IFfcDetailedTableExportService exportService,
        UserManager<ApplicationUser> userManager,
        ILogger<MapTableDetailedModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ffcQueryService = ffcQueryService ?? throw new ArgumentNullException(nameof(ffcQueryService));
        _ffcProgressService = ffcProgressService ?? throw new ArgumentNullException(nameof(ffcProgressService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // SECTION: Filter state
    public long? CountryId { get; private set; }

    public string? CountryIso3 { get; private set; }

    public short? Year { get; private set; }

    public string? CountryName { get; private set; }

    public bool HasFilters => CountryId.HasValue || Year.HasValue || !string.IsNullOrWhiteSpace(CountryIso3);

    public string? FilterSummary { get; private set; }

    public string ExportScopeLabel { get; private set; } = "Complete portfolio";

    public string GeneratedAtIstDisplay { get; private set; } = string.Empty;

    public bool ShowWordExportModal { get; private set; }

    // SECTION: View model
    public IReadOnlyList<FfcDetailedGroupVm> Groups { get; private set; } = Array.Empty<FfcDetailedGroupVm>();

    [BindProperty]
    public WordExportInputModel WordExport { get; set; } = new();

    // SECTION: Request handlers
    public async Task OnGetAsync(long? countryId, short? year, string? countryIso3, CancellationToken cancellationToken)
    {
        await LoadPageAsync(countryId, year, countryIso3, cancellationToken);
    }

    public async Task<IActionResult> OnGetDataAsync(
        long? countryId,
        short? year,
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        await ResolveScopeAsync(countryId, year, countryIso3, cancellationToken);
        return new JsonResult(await LoadGroupsAsync(cancellationToken));
    }

    // Retained for compatibility with existing bookmarks and links.
    public Task<IActionResult> OnGetExportAsync(
        long? countryId,
        short? year,
        string? countryIso3,
        CancellationToken cancellationToken)
        => OnGetExportExcelAsync(countryId, year, countryIso3, cancellationToken);

    public async Task<IActionResult> OnGetExportExcelAsync(
        long? countryId,
        short? year,
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        await ResolveScopeAsync(countryId, year, countryIso3, cancellationToken);
        var groups = await LoadGroupsAsync(cancellationToken);
        var export = _exportService.BuildExcel(CreateExportContext(groups, handlingMarking: null));

        return File(export.Content, export.ContentType, export.FileName);
    }

    public async Task<IActionResult> OnPostExportWordAsync(
        long? countryId,
        short? year,
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        await ResolveScopeAsync(countryId, year, countryIso3, cancellationToken);

        if (!ModelState.IsValid)
        {
            await CompletePageStateAsync(cancellationToken);
            ShowWordExportModal = true;
            return Page();
        }

        var groups = await LoadGroupsAsync(cancellationToken);
        var handlingMarking = NormalizeHandlingMarking(WordExport.HandlingMarking);
        var export = _exportService.BuildWord(CreateExportContext(groups, handlingMarking));

        return File(export.Content, export.ContentType, export.FileName);
    }

    // SECTION: Inline editing handlers
    public async Task<IActionResult> OnPostUpdateOverallRemarksAsync(
        [FromBody] UpdateOverallRemarksRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("HoD"))
        {
            return Forbid();
        }

        if (request is null)
        {
            return BadRequest(new { message = "Request payload is missing." });
        }

        if (request.FfcRecordId <= 0)
        {
            return BadRequest(new { message = "Invalid record identifier." });
        }

        var normalized = NormalizeRemark(request.OverallRemarks);
        if (normalized.Length > OverallRemarksMaxLength)
        {
            return BadRequest(new { message = $"Overall status must be {OverallRemarksMaxLength} characters or fewer." });
        }

        var record = await _db.FfcRecords
            .FirstOrDefaultAsync(item => item.Id == request.FfcRecordId, cancellationToken);

        if (record is null)
        {
            return NotFound(new { message = "Record not found." });
        }

        record.OverallRemarks = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new JsonResult(new
        {
            ok = true,
            overallRemarks = normalized,
            renderedOverallRemarks = FormatRemarkForDisplay(normalized),
            updatedAtUtc = record.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            updatedBy = User.Identity?.Name
        });
    }

    public async Task<IActionResult> OnPostUpdateProgressAsync(
        [FromBody] UpdateProgressRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("HoD"))
        {
            return Forbid();
        }

        if (request is null)
        {
            return BadRequest(new { message = "Request payload is missing." });
        }

        var actor = await BuildRemarkActorContextAsync(cancellationToken);

        try
        {
            var result = await _ffcProgressService.UpdateProgressAsync(
                new FfcProgressUpdateCommand(
                    FfcProjectId: request.FfcProjectId,
                    RequestedLinkedProjectId: request.LinkedProjectId,
                    ExternalRemarkId: request.ExternalRemarkId,
                    ProgressText: request.ProgressText,
                    Actor: actor),
                cancellationToken);

            return new JsonResult(new
            {
                ok = true,
                progressText = result.ProgressText,
                renderedProgressText = FormatRemarkForDisplay(result.ProgressText),
                updatedAtUtc = result.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                updatedBy = User.Identity?.Name,
                externalRemarkId = result.ExternalRemarkId
            });
        }
        catch (FfcProgressNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (FfcProgressValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException ex)
        {
            LogProgressUpdateFailure(ex, request, request.LinkedProjectId);
            return BadRequest(new
            {
                message = "Unable to save progress due to a database constraint. See server logs for details."
            });
        }
        catch (Exception ex)
        {
            LogProgressUpdateFailure(ex, request, request.LinkedProjectId);
            return StatusCode(500, new
            {
                ok = false,
                message = "Unable to save. See server logs for details."
            });
        }
    }

    // SECTION: Page state
    private async Task LoadPageAsync(
        long? countryId,
        short? year,
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        await ResolveScopeAsync(countryId, year, countryIso3, cancellationToken);
        await CompletePageStateAsync(cancellationToken);
    }

    private async Task CompletePageStateAsync(CancellationToken cancellationToken)
    {
        FilterSummary = BuildFilterSummary();
        ExportScopeLabel = string.IsNullOrWhiteSpace(FilterSummary) ? "Complete portfolio" : FilterSummary!;

        var generatedAtIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneHelper.GetIst());
        GeneratedAtIstDisplay = generatedAtIst.ToString("dd MMM yyyy, HH:mm 'IST'", CultureInfo.InvariantCulture);
        Groups = await LoadGroupsAsync(cancellationToken);

        FfcBreadcrumbs.Set(
            ViewData,
            ("FFC Proposals", Url.Page("/FFC/Index", new { area = "ProjectOfficeReports" })),
            ("Detailed table", null));
    }

    private async Task ResolveScopeAsync(
        long? countryId,
        short? year,
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        CountryIso3 = string.IsNullOrWhiteSpace(countryIso3)
            ? null
            : countryIso3.Trim().ToUpperInvariant();
        CountryId = await ResolveCountryIdAsync(countryId, CountryIso3, cancellationToken);
        Year = year;

        if (CountryId.HasValue)
        {
            var country = await _db.FfcCountries
                .AsNoTracking()
                .Where(item => item.Id == CountryId.Value)
                .Select(item => new { item.Name, item.IsoCode })
                .FirstOrDefaultAsync(cancellationToken);

            CountryName = country?.Name;
            CountryIso3 = country?.IsoCode?.Trim().ToUpperInvariant() ?? CountryIso3;
        }

        FilterSummary = BuildFilterSummary();
        ExportScopeLabel = string.IsNullOrWhiteSpace(FilterSummary) ? "Complete portfolio" : FilterSummary!;
    }

    private async Task<IReadOnlyList<FfcDetailedGroupVm>> LoadGroupsAsync(CancellationToken cancellationToken)
    {
        // An unknown ISO code must not silently fall back to the complete portfolio.
        if (!string.IsNullOrWhiteSpace(CountryIso3) && !CountryId.HasValue)
        {
            return Array.Empty<FfcDetailedGroupVm>();
        }

        var (rangeFrom, rangeTo) = ResolveRange();
        return await _ffcQueryService.GetDetailedGroupsAsync(
            rangeFrom,
            rangeTo,
            incompleteOnly: false,
            CountryId,
            Year,
            applyYearFilter: true,
            cancellationToken: cancellationToken);
    }

    private FfcDetailedTableExportContext CreateExportContext(
        IReadOnlyList<FfcDetailedGroupVm> groups,
        string? handlingMarking)
        => new(
            ScopeLabel: ExportScopeLabel,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            HandlingMarking: handlingMarking,
            Groups: groups);

    private string? BuildFilterSummary()
    {
        if (!HasFilters)
        {
            return null;
        }

        var parts = new List<string>();

        if (CountryId.HasValue)
        {
            parts.Add(string.IsNullOrWhiteSpace(CountryName) ? "Selected country" : CountryName!);
        }
        else if (!string.IsNullOrWhiteSpace(CountryIso3))
        {
            parts.Add(CountryIso3);
        }

        if (Year.HasValue)
        {
            parts.Add(Year.Value.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(" · ", parts);
    }

    private (DateOnly From, DateOnly To) ResolveRange()
    {
        if (Year.HasValue)
        {
            return (new DateOnly(Year.Value, 1, 1), new DateOnly(Year.Value, 12, 31));
        }

        return (DateOnly.MinValue, DateOnly.MaxValue);
    }

    // SECTION: Lookup helpers
    private async Task<long?> ResolveCountryIdAsync(
        long? countryId,
        string? countryIso3,
        CancellationToken cancellationToken)
    {
        if (countryId.HasValue)
        {
            return countryId;
        }

        if (string.IsNullOrWhiteSpace(countryIso3))
        {
            return null;
        }

        var iso = countryIso3.Trim().ToUpperInvariant();
        var matchedId = await _db.FfcCountries
            .AsNoTracking()
            .Where(country => country.IsoCode == iso)
            .Select(country => country.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return matchedId == 0 ? null : matchedId;
    }

    // SECTION: Normalisation helpers
    private static string NormalizeRemark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string FormatRemarkForDisplay(string? value)
        => NormalizeRemark(value);

    private static string? NormalizeHandlingMarking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private Task<RemarkActorContext?> BuildRemarkActorContextAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult<RemarkActorContext?>(null);
        }

        var roles = new List<RemarkActorRole>();
        if (User.IsInRole("Admin"))
        {
            roles.Add(RemarkActorRole.Administrator);
        }

        if (User.IsInRole("HoD"))
        {
            roles.Add(RemarkActorRole.HeadOfDepartment);
        }

        if (roles.Count == 0)
        {
            return Task.FromResult<RemarkActorContext?>(null);
        }

        var primary = roles.Contains(RemarkActorRole.Administrator)
            ? RemarkActorRole.Administrator
            : RemarkActorRole.HeadOfDepartment;

        return Task.FromResult<RemarkActorContext?>(new RemarkActorContext(userId, primary, roles));
    }

    private void LogProgressUpdateFailure(Exception ex, UpdateProgressRequest request, int? linkedProjectId)
    {
        var userId = _userManager.GetUserId(User);
        var roles = new List<string>();
        if (User.IsInRole("Admin"))
        {
            roles.Add("Admin");
        }

        if (User.IsInRole("HoD"))
        {
            roles.Add("HoD");
        }

        _logger.LogError(
            ex,
            "Failed to update FFC progress. FfcProjectId={FfcProjectId}, LinkedProjectId={LinkedProjectId}, ExternalRemarkId={ExternalRemarkId}, UserId={UserId}, Roles={Roles}",
            request.FfcProjectId,
            linkedProjectId,
            request.ExternalRemarkId,
            userId ?? string.Empty,
            roles.Count == 0 ? "None" : string.Join(",", roles));

        if (ex is DbUpdateException { InnerException: Npgsql.PostgresException postgresException })
        {
            _logger.LogError(
                "PostgresException SqlState={SqlState}, Constraint={Constraint}, Detail={Detail}",
                postgresException.SqlState,
                postgresException.ConstraintName,
                postgresException.Detail);
        }
    }

    public sealed class WordExportInputModel
    {
        [Display(Name = "Handling / classification marking")]
        [StringLength(80, ErrorMessage = "The marking must be 80 characters or fewer.")]
        [RegularExpression(@"^[^\r\n]*$", ErrorMessage = "The marking must be entered on one line.")]
        public string? HandlingMarking { get; set; }
    }

    public sealed class UpdateOverallRemarksRequest
    {
        [Required]
        public long FfcRecordId { get; set; }

        public string? OverallRemarks { get; set; }
    }

    public sealed class UpdateProgressRequest
    {
        [Required]
        public long FfcProjectId { get; set; }

        public string? ProgressText { get; set; }

        public int? LinkedProjectId { get; set; }

        public int? ExternalRemarkId { get; set; }
    }
}
