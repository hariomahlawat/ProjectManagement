using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Pages.Projects.Arpp;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly IArppLibraryService _libraryService;
    private readonly IArppExportService _exportService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IClock _clock;

    public IndexModel(
        IArppLibraryService libraryService,
        IArppExportService exportService,
        IAuthorizationService authorizationService,
        IClock clock)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    [BindProperty(SupportsGet = true)]
    public int? FinancialYearStart { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? IssueId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    public ArppLibraryNavigation Navigation { get; private set; }
        = new(Array.Empty<ArppLibraryFinancialYear>(), 0);

    public ArppLibraryDocument? Document { get; private set; }

    public ArppLibraryCurrentPosition? CurrentPosition { get; private set; }

    public int? SelectedFinancialYearStart { get; private set; }

    public bool CanManageArpp { get; private set; }

    public bool HasPublishedDocuments => Navigation.PublishedDocumentCount > 0;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Query = ArppLibrarySearch.Normalize(Query);
        Navigation = await _libraryService.GetNavigationAsync(Query, cancellationToken);
        CanManageArpp = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ManageArpp)).Succeeded;

        if (!HasPublishedDocuments)
        {
            return Page();
        }

        if (IssueId.HasValue)
        {
            var selectedIssueId = IssueId.Value;
            if (Query is not null)
            {
                var visibleIssueIds = Navigation.FinancialYears
                    .SelectMany(year => year.Documents)
                    .Select(document => document.IssueId)
                    .ToHashSet();

                if (!visibleIssueIds.Contains(selectedIssueId))
                {
                    selectedIssueId = Navigation.FinancialYears
                        .SelectMany(year => year.Documents)
                        .Select(document => document.IssueId)
                        .First();
                    IssueId = selectedIssueId;
                }
            }

            var document = await _libraryService.GetDocumentAsync(selectedIssueId, cancellationToken);
            if (document is null)
            {
                return NotFound();
            }

            Document = FilterDocumentRows(document, Query);
            SelectedFinancialYearStart = document.FinancialYearStart;
            return Page();
        }

        var availableYears = Navigation.FinancialYears
            .Select(year => year.FinancialYearStart)
            .ToHashSet();
        SelectedFinancialYearStart = FinancialYearStart.HasValue && availableYears.Contains(FinancialYearStart.Value)
            ? FinancialYearStart.Value
            : Navigation.FinancialYears[0].FinancialYearStart;

        CurrentPosition = await _libraryService.GetCurrentPositionAsync(
            SelectedFinancialYearStart.Value,
            Query,
            cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnGetAttachmentAsync(long id, CancellationToken cancellationToken)
    {
        var download = await _libraryService.OpenAttachmentAsync(id, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        var fileResult = File(download.Content, download.ContentType, download.DownloadFileName);
        fileResult.EnableRangeProcessing = true;
        return fileResult;
    }

    public async Task<IActionResult> OnGetExcelAsync(long id, CancellationToken cancellationToken)
    {
        var document = await _libraryService.GetDocumentAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var export = _exportService.BuildExcel(
            ToIssueDetails(document),
            _clock.UtcNow.ToUniversalTime(),
            includeRecordControlMetadata: false,
            includePrismLinkageColumns: false);
        return File(export.Content, export.ContentType, export.FileName);
    }

    private static ArppLibraryDocument FilterDocumentRows(
        ArppLibraryDocument document,
        string? query)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            document.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return document;
        }

        var filteredRows = document.Rows
            .Where(row => ArppLibrarySearch.Matches(row, query))
            .ToArray();

        return document with { Rows = filteredRows };
    }

    private static ArppIssueDetails ToIssueDetails(ArppLibraryDocument document)
    {
        var entries = document.Rows
            .Select(row => new ArppEntryDetails(
                row.EntryId,
                row.SortOrder,
                row.SerialNumber,
                row.PppNumber,
                row.ProjectReference,
                row.ProjectId,
                row.ProjectName,
                null,
                row.ProjectStatus,
                row.Category,
                row.IpaCost,
                null,
                row.Cfa,
                null,
                row.Fund,
                null,
                row.DfpdsSchedule,
                string.Empty))
            .ToArray();

        var summary = Enum.GetValues<ArppCategory>()
            .ToDictionary(
                category => category,
                category => new ArppCategorySummary(
                    category,
                    entries.Count(entry => entry.Category == category),
                    entries.Where(entry => entry.Category == category).Sum(entry => entry.IpaCost)));

        return new ArppIssueDetails(
            document.IssueId,
            document.FinancialYearStart,
            document.Kind,
            document.IssueSequence,
            document.Name,
            document.IssueDate,
            string.Empty,
            entries,
            entries.Sum(entry => entry.IpaCost),
            summary,
            entries.Count(entry => entry.ProjectId.HasValue),
            entries.Count(entry => !entry.ProjectId.HasValue),
            document.PublishedAtUtc,
            document.PublishedAtUtc,
            new ArppAttachmentDetails(
                0,
                document.Attachment.OriginalFileName,
                document.Attachment.ContentType,
                document.Attachment.SizeBytes,
                document.Attachment.Sha256,
                string.Empty,
                document.PublishedAtUtc,
                string.Empty),
            true,
            document.PublishedAtUtc,
            null,
            null,
            null);
    }
}
