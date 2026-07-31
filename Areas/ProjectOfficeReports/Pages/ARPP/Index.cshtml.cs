using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
public sealed class IndexModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IArppLibraryService _libraryService;
    private readonly IAuthorizationService _authorizationService;

    public IndexModel(
        IArppReadService readService,
        IArppLibraryService libraryService,
        IAuthorizationService authorizationService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    [BindProperty(SupportsGet = true, Name = "fy")]
    public int? FinancialYearStart { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public ArppRegisterResult Register { get; private set; } = new(
        [], [], 0, 0, 0m, 0m, 0m, 0, 0, 0, 0);

    public bool CanManage { get; private set; }

    /// <summary>
    /// Authoritative, organisation-visible position derived only from verified published
    /// snapshots. It is deliberately independent of working-copy values and free-text
    /// administration filters so the financial-year heading never presents editable data
    /// as the published position.
    /// </summary>
    public IReadOnlyDictionary<int, PublishedFinancialYearSummary> PublishedPositions { get; private set; }
        = new Dictionary<int, PublishedFinancialYearSummary>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Register = await _readService.GetRegisterAsync(
            FinancialYearStart,
            Query,
            cancellationToken);

        var publishedPositions = new Dictionary<int, PublishedFinancialYearSummary>();
        foreach (var group in Register.FinancialYears)
        {
            var currentPosition = await _libraryService.GetCurrentPositionAsync(
                group.FinancialYearStart,
                query: null,
                cancellationToken);

            if (currentPosition is null)
            {
                continue;
            }

            publishedPositions[group.FinancialYearStart] = new PublishedFinancialYearSummary(
                currentPosition.ApprovedIpaValue,
                currentPosition.DelistedIpaValue,
                currentPosition.ApprovedRows.Count,
                currentPosition.DelistedRows.Count,
                currentPosition.TotalUnlinkedDocumentRows);
        }

        PublishedPositions = publishedPositions;

        CanManage = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            ProjectOfficeReportsPolicies.ManageArpp)).Succeeded;
    }
}


public sealed record PublishedFinancialYearSummary(
    decimal ApprovedIpaValue,
    decimal DelistedIpaValue,
    int ApprovedProjectCount,
    int DelistedProjectCount,
    int UnlinkedDocumentRowCount);
