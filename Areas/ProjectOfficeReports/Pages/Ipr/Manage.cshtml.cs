using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.Edit)]
public sealed class ManageModel : PageModel
{
    private static readonly IReadOnlyDictionary<string, string[]> ValidationErrorFieldMap =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Filed date cannot be in the future."] = new[] { nameof(IndexModel.RecordInput.FiledOn) },
            ["Grant date cannot be in the future."] = new[] { nameof(IndexModel.RecordInput.GrantedOn) },
            ["Filed date is required once the record is not under filing."] = new[] { nameof(IndexModel.RecordInput.FiledOn) },
            ["Grant date is required once the record is granted."] = new[] { nameof(IndexModel.RecordInput.GrantedOn) },
            ["Grant date cannot be provided without a filing date."] = new[] { nameof(IndexModel.RecordInput.FiledOn), nameof(IndexModel.RecordInput.GrantedOn) },
            ["Grant date cannot be earlier than the filing date."] = new[] { nameof(IndexModel.RecordInput.FiledOn), nameof(IndexModel.RecordInput.GrantedOn) },
            ["An IPR with the same filing number and type already exists."] = new[] { nameof(IndexModel.RecordInput.FilingNumber) },
            ["Filing number is required."] = new[] { nameof(IndexModel.RecordInput.FilingNumber) }
        };

    private readonly ApplicationDbContext _db;
    private readonly IIprReadService _readService;
    private readonly IIprWriteService _writeService;

    private string? _query;

    public ManageModel(ApplicationDbContext db, IIprReadService readService, IIprWriteService writeService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
    }

    [BindProperty(SupportsGet = true)]
    public string? Query
    {
        get => _query;
        set => _query = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty]
    public IndexModel.RecordInput Input { get; set; } = CreateDefaultInput();

    public IReadOnlyList<RecordRow> Records { get; private set; } = Array.Empty<RecordRow>();

    public IReadOnlyList<SelectListItem> TypeFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> StatusFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Input = CreateDefaultInput();

        if (EditId.HasValue)
        {
            var record = await _readService.GetAsync(EditId.Value, cancellationToken);
            if (record is null)
            {
                TempData["ToastError"] = "The selected IPR record could not be found.";
                EditId = null;
            }
            else
            {
                Input = MapToInput(record);
            }
        }

        await LoadSelectListsAsync(cancellationToken);
        await LoadRecordsAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        EditId = null;

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        if (Input.Type is null)
        {
            ModelState.AddModelError("Input.Type", "Select a type.");
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError("Input.Status", "Select a status.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            await _writeService.CreateAsync(entity, cancellationToken);
            TempData["ToastMessage"] = "IPR record created.";
            return RedirectToPage("./Manage", new { Query });
        }
        catch (InvalidOperationException ex)
        {
            if (!TryAddInputValidationErrors(ex.Message))
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (Input.Id.HasValue)
        {
            EditId = Input.Id;
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        if (!Input.Id.HasValue)
        {
            ModelState.AddModelError(string.Empty, "The record could not be found.");
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        if (Input.Type is null)
        {
            ModelState.AddModelError("Input.Type", "Select a type.");
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError("Input.Status", "Select a status.");
        }

        var rowVersion = DecodeRowVersion(Input.RowVersion);
        if (rowVersion is null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            entity.RowVersion = rowVersion!;

            var updated = await _writeService.UpdateAsync(entity, cancellationToken);
            if (updated is null)
            {
                TempData["ToastError"] = "The selected IPR record could not be found.";
                return RedirectToPage("./Manage", new { Query });
            }

            TempData["ToastMessage"] = "IPR record updated.";
            return RedirectToPage("./Manage", new { Query });
        }
        catch (InvalidOperationException ex)
        {
            if (!TryAddInputValidationErrors(ex.Message))
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string rowVersion, CancellationToken cancellationToken)
    {
        var decoded = DecodeRowVersion(rowVersion);
        if (decoded is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Manage", new { Query });
        }

        try
        {
            var deleted = await _writeService.DeleteAsync(id, decoded, cancellationToken);
            if (!deleted)
            {
                TempData["ToastError"] = "The selected IPR record could not be found.";
            }
            else
            {
                TempData["ToastMessage"] = "IPR record deleted.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage("./Manage", new { Query });
    }

    private async Task LoadRecordsAsync(CancellationToken cancellationToken)
    {
        var query = _db.IprRecords
            .AsNoTracking()
            .Include(r => r.Project)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var trimmed = Query.Trim();
            query = query.Where(r =>
                EF.Functions.ILike(r.IprFilingNumber, $"%{trimmed}%") ||
                (r.Title != null && EF.Functions.ILike(r.Title, $"%{trimmed}%")));
        }

        var items = await query
            .OrderByDescending(r => r.FiledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.Id)
            .Select(r => new RecordRow(
                r.Id,
                r.IprFilingNumber,
                r.Title,
                GetTypeLabel(r.Type),
                GetStatusLabel(r.Status),
                r.Project != null ? r.Project.Name : null,
                Convert.ToBase64String(r.RowVersion)))
            .ToListAsync(cancellationToken);

        Records = items;
    }

    private async Task LoadSelectListsAsync(CancellationToken cancellationToken)
    {
        var supportedTypes = new[] { IprType.Patent, IprType.Copyright };

        TypeFormOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Input.Type.HasValue && Input.Type.Value == type
            })
            .ToList();

        StatusFormOptions = Enum.GetValues<IprStatus>()
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Input.Status.HasValue && Input.Status.Value == status
            })
            .ToList();

        var projectItems = await _db.Projects.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString(CultureInfo.InvariantCulture))
            {
                Selected = Input.ProjectId.HasValue && Input.ProjectId.Value == p.Id
            })
            .ToListAsync(cancellationToken);

        var options = new List<SelectListItem>
        {
            new("No project", string.Empty)
            {
                Selected = !Input.ProjectId.HasValue
            }
        };

        options.AddRange(projectItems);
        ProjectOptions = options;
    }

    private static IndexModel.RecordInput CreateDefaultInput()
    {
        return new IndexModel.RecordInput
        {
            Type = IprType.Patent,
            Status = IprStatus.FilingUnderProcess
        };
    }

    private static IndexModel.RecordInput MapToInput(IprRecord record)
    {
        return new IndexModel.RecordInput
        {
            Id = record.Id,
            FilingNumber = record.IprFilingNumber,
            Title = record.Title,
            Notes = record.Notes,
            Type = record.Type,
            Status = record.Status,
            FiledBy = record.FiledBy,
            FiledOn = record.FiledAtUtc.HasValue
                ? DateOnly.FromDateTime(record.FiledAtUtc.Value.UtcDateTime)
                : null,
            GrantedOn = record.GrantedAtUtc.HasValue
                ? DateOnly.FromDateTime(record.GrantedAtUtc.Value.UtcDateTime)
                : null,
            ProjectId = record.ProjectId,
            RowVersion = Convert.ToBase64String(record.RowVersion)
        };
    }

    private static IprRecord ToEntity(IndexModel.RecordInput input)
    {
        return new IprRecord
        {
            Id = input.Id ?? 0,
            IprFilingNumber = input.FilingNumber?.Trim() ?? string.Empty,
            Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Type = input.Type ?? IprType.Patent,
            Status = input.Status ?? IprStatus.FilingUnderProcess,
            FiledBy = string.IsNullOrWhiteSpace(input.FiledBy) ? null : input.FiledBy.Trim(),
            FiledAtUtc = input.FiledOn.HasValue
                ? new DateTimeOffset(input.FiledOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            GrantedAtUtc = input.GrantedOn.HasValue
                ? new DateTimeOffset(input.GrantedOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            ProjectId = input.ProjectId
        };
    }

    private static string GetTypeLabel(IprType type)
        => type switch
        {
            IprType.Patent => "Patent",
            IprType.Copyright => "Copyright",
            _ => type.ToString()
        };

    private static string GetStatusLabel(IprStatus status)
        => status switch
        {
            IprStatus.FilingUnderProcess => "Filing under process",
            IprStatus.Filed => "Filed",
            IprStatus.Granted => "Granted",
            IprStatus.Rejected => "Rejected",
            IprStatus.Withdrawn => "Withdrawn",
            _ => status.ToString()
        };

    private bool TryAddInputValidationErrors(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (!ValidationErrorFieldMap.TryGetValue(message, out var fields))
        {
            return false;
        }

        foreach (var field in fields)
        {
            ModelState.AddModelError($"{nameof(Input)}.{field}", message);
        }

        return true;
    }

    private static byte[]? DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public sealed record RecordRow(
        int Id,
        string FilingNumber,
        string? Title,
        string TypeLabel,
        string StatusLabel,
        string? ProjectName,
        string RowVersion);
}
