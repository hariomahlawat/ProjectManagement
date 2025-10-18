using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Admin;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageProliferationImports)]
public sealed class IndexModel : PageModel
{
    private const string RejectionCachePrefix = "ProliferationImport:";
    private readonly IProliferationYearlyImportService _yearlyImportService;
    private readonly IProliferationGranularImportService _granularImportService;
    private readonly IProliferationExportService _exportService;
    private readonly IMemoryCache _cache;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IProliferationYearlyImportService yearlyImportService,
        IProliferationGranularImportService granularImportService,
        IProliferationExportService exportService,
        IMemoryCache cache,
        UserManager<ApplicationUser> userManager,
        ILogger<IndexModel> logger)
    {
        _yearlyImportService = yearlyImportService ?? throw new ArgumentNullException(nameof(yearlyImportService));
        _granularImportService = granularImportService ?? throw new ArgumentNullException(nameof(granularImportService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public YearlyImportInput Yearly { get; set; } = new();

    [BindProperty]
    public GranularImportInput Granular { get; set; } = new();

    [BindProperty]
    public ExportInput Export { get; set; } = new();

    public PendingRejectionDownload? PendingRejection { get; private set; }

    public void OnGet()
    {
        LoadPendingRejection();
    }

    public async Task<IActionResult> OnPostImportYearlyAsync(CancellationToken cancellationToken)
    {
        LoadPendingRejection();

        if (Yearly.File is null || Yearly.File.Length == 0)
        {
            ModelState.AddModelError(nameof(Yearly) + "." + nameof(Yearly.File), "Select a CSV file to upload.");
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        await using var stream = Yearly.File.OpenReadStream();
        var request = new ProliferationYearlyImportRequest(
            stream,
            Yearly.File.FileName,
            Yearly.Source,
            userId);

        var result = await _yearlyImportService.ImportAsync(request, cancellationToken);
        HandleImportResult(result, Yearly.File.FileName, Yearly.Source.ToString());

        if (result.RejectionFile is not null)
        {
            StoreRejection(result.RejectionFile);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportGranularAsync(CancellationToken cancellationToken)
    {
        LoadPendingRejection();

        if (Granular.File is null || Granular.File.Length == 0)
        {
            ModelState.AddModelError(nameof(Granular) + "." + nameof(Granular.File), "Select a CSV file to upload.");
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        await using var stream = Granular.File.OpenReadStream();
        var request = new ProliferationGranularImportRequest(
            stream,
            Granular.File.FileName,
            ProliferationSource.Internal,
            userId);

        var result = await _granularImportService.ImportAsync(request, cancellationToken);
        HandleImportResult(result, Granular.File.FileName, "SDD granular");

        if (result.RejectionFile is not null)
        {
            StoreRejection(result.RejectionFile);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        LoadPendingRejection();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var request = new ProliferationExportRequest(
            Export.Source,
            Export.YearFrom,
            Export.YearTo,
            Export.SponsoringUnitId,
            Export.SimulatorUserId,
            Export.SearchTerm,
            userId);

        var result = await _exportService.ExportAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            TempData["ToastError"] = result.Errors.Count > 0
                ? result.Errors[0]
                : "Export failed. Please try again.";
            return Page();
        }

        TempData["ToastMessage"] = "Tracker export generated.";
        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    public IActionResult OnGetDownloadRejection(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["ToastError"] = "The rejection file could not be found.";
            return RedirectToPage();
        }

        if (!_cache.TryGetValue<ProliferationImportFile>(BuildCacheKey(token), out var file) || file is null)
        {
            TempData["ToastError"] = "The rejection file has expired.";
            return RedirectToPage();
        }

        _cache.Remove(BuildCacheKey(token));
        TempData.Remove("ImportRejectionToken");
        TempData.Remove("ImportRejectionFileName");

        return File(file.Content, file.ContentType, file.FileName);
    }

    private void HandleImportResult(ProliferationImportResult result, string fileName, string sourceLabel)
    {
        var displayName = string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName;
        if (result.ImportedRows > 0)
        {
            TempData["ToastMessage"] = $"Imported {result.ImportedRows} row(s) from {displayName}.";
        }
        else
        {
            TempData["ToastWarning"] = $"No rows were imported from {displayName}.";
        }

        if (result.Errors.Count > 0)
        {
            TempData["ToastWarning"] = result.ImportedRows > 0
                ? $"{result.Errors.Count} row(s) from {sourceLabel} could not be imported."
                : $"All rows were rejected. {result.Errors.Count} issue(s) detected.";

            _logger.LogWarning(
                "Proliferation import completed with {ErrorCount} error(s). Source: {SourceLabel}, File: {FileName}.",
                result.Errors.Count,
                sourceLabel,
                displayName);
        }
        else
        {
            _logger.LogInformation(
                "Proliferation import completed successfully. Imported {ImportedRows} row(s) from {FileName}.",
                result.ImportedRows,
                displayName);
        }
    }

    private void StoreRejection(ProliferationImportFile file)
    {
        if (TempData.Peek("ImportRejectionToken") is string existingToken && !string.IsNullOrWhiteSpace(existingToken))
        {
            _cache.Remove(BuildCacheKey(existingToken));
        }

        var token = Guid.NewGuid().ToString("N");
        _cache.Set(BuildCacheKey(token), file, TimeSpan.FromMinutes(15));
        TempData["ImportRejectionToken"] = token;
        TempData["ImportRejectionFileName"] = file.FileName;
        PendingRejection = new PendingRejectionDownload(token, file.FileName);
    }

    private void LoadPendingRejection()
    {
        if (TempData.Peek("ImportRejectionToken") is string token && !string.IsNullOrWhiteSpace(token))
        {
            var fileName = TempData.Peek("ImportRejectionFileName") as string ?? "rejection.csv";
            PendingRejection = new PendingRejectionDownload(token, fileName);
        }
    }

    private static string BuildCacheKey(string token) => RejectionCachePrefix + token;

    public sealed class YearlyImportInput
    {
        [Required]
        public ProliferationSource Source { get; set; } = ProliferationSource.Internal;

        [Required]
        public IFormFile? File { get; set; }
    }

    public sealed class GranularImportInput
    {
        [Required]
        public IFormFile? File { get; set; }
    }

    public sealed class ExportInput
    {
        public ProliferationSource? Source { get; set; }

        [Range(2000, 9999, ErrorMessage = "Enter a valid starting year.")]
        public int? YearFrom { get; set; }

        [Range(2000, 9999, ErrorMessage = "Enter a valid ending year.")]
        public int? YearTo { get; set; }

        public int? SponsoringUnitId { get; set; }

        [Display(Name = "Simulator user ID")]
        public string? SimulatorUserId { get; set; }

        [Display(Name = "Project search")]
        public string? SearchTerm { get; set; }
    }

    public sealed record PendingRejectionDownload(string Token, string FileName);
}
