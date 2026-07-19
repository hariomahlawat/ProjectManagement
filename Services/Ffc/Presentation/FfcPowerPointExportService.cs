using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ffc.Presentation;

public sealed class FfcPowerPointExportService : IFfcPowerPointExportService
{
    public const string PowerPointContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly IFfcPresentationDataService _dataService;
    private readonly IFfcSlideComposer _slideComposer;

    public FfcPowerPointExportService(
        IFfcPresentationDataService dataService,
        IFfcSlideComposer slideComposer)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _slideComposer = slideComposer ?? throw new ArgumentNullException(nameof(slideComposer));
    }

    public async Task<FfcPowerPointExportResult> GenerateAsync(
        FfcPowerPointExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var data = await _dataService.GetAsync(request, cancellationToken);
        if (data.Countries.Count == 0)
        {
            throw new InvalidOperationException("No FFC records match the selected PowerPoint export scope.");
        }

        var (content, slideCount) = _slideComposer.Compose(data);
        var fileName = BuildFileName(request, data);
        return new FfcPowerPointExportResult(
            content,
            fileName,
            PowerPointContentType,
            slideCount);
    }

    private static void Validate(FfcPowerPointExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("A presentation title is required.", nameof(request));
        }
        if (request.Title.Trim().Length > 120)
        {
            throw new ArgumentException("The presentation title cannot exceed 120 characters.", nameof(request));
        }
        if (request.Subtitle?.Trim().Length > 180)
        {
            throw new ArgumentException("The presentation subtitle cannot exceed 180 characters.", nameof(request));
        }
        if (request.HandlingMarking?.Trim().Length > 80)
        {
            throw new ArgumentException("The handling marking cannot exceed 80 characters.", nameof(request));
        }
        if (request.Scope == FfcExportScope.SelectedCountries &&
            request.SelectedCountryIds.All(countryId => countryId <= 0))
        {
            throw new ArgumentException("Select at least one country for the PowerPoint export.", nameof(request));
        }
    }

    private static string BuildFileName(
        FfcPowerPointExportRequest request,
        FfcPresentationData data)
    {
        var components = new StringBuilder("FFC_Portfolio");
        if (request.Scope != FfcExportScope.CompletePortfolio && request.Year.HasValue)
        {
            components.Append('_').Append(request.Year.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (data.Countries.Count == 1)
        {
            components.Append('_').Append(SafeFileToken(data.Countries[0].CountryName));
        }
        components.Append('_').Append(request.RequestedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        components.Append(".pptx");
        return components.ToString();
    }

    private static string SafeFileToken(string value)
    {
        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        var normalized = new string(characters).Trim('_');
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }
        return string.IsNullOrWhiteSpace(normalized) ? "Selection" : normalized;
    }
}
