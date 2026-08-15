namespace ProjectManagement.Services.Compendiums;

public enum CompendiumProgrammeModuleKind
{
    ArmsServices = 0,
    ProliferationCost = 1,
    Ipr = 2,
    TechnologyTransfer = 3
}

public enum CompendiumIprVisualState
{
    Filed = 0,
    Granted = 1,
    Mixed = 2
}

public sealed record CompendiumProgrammeModuleDto(
    CompendiumProgrammeModuleKind Kind,
    string Label,
    string Value,
    string IconKey,
    string Tone,
    CompendiumIprVisualState? IprState = null);

/// <summary>
/// Produces the authoritative programme-information projection shared by the browser proof and PDF.
/// Optional modules are omitted rather than rendered as empty publication furniture.
/// </summary>
public static class CompendiumProgrammeInformation
{
    public static IReadOnlyList<CompendiumProgrammeModuleDto> Resolve(
        string? sponsoringLineDirectorate,
        string? proliferationCostDisplay,
        IReadOnlyList<CompendiumIprCredentialDto>? iprCredentials,
        CompendiumTechnologyTransferDto? technologyTransfer)
    {
        var modules = new List<CompendiumProgrammeModuleDto>(4);

        var cleanSponsoringLineDirectorate = NormalizeOptional(sponsoringLineDirectorate);
        if (cleanSponsoringLineDirectorate is not null && !IsNotRecorded(cleanSponsoringLineDirectorate))
        {
            modules.Add(new CompendiumProgrammeModuleDto(
                CompendiumProgrammeModuleKind.ArmsServices,
                "Arms / Services",
                cleanSponsoringLineDirectorate,
                "arms-services",
                "maroon"));
        }

        var cleanCost = NormalizeOptional(proliferationCostDisplay);
        if (cleanCost is not null && !IsNotRecorded(cleanCost))
        {
            modules.Add(new CompendiumProgrammeModuleDto(
                CompendiumProgrammeModuleKind.ProliferationCost,
                "Proliferation cost",
                cleanCost,
                "proliferation-cost",
                "green"));
        }

        var validIpr = (iprCredentials ?? Array.Empty<CompendiumIprCredentialDto>())
            .Where(item => IsFiled(item.Status) || IsGranted(item.Status))
            .ToArray();
        if (validIpr.Length > 0)
        {
            var hasFiled = validIpr.Any(item => IsFiled(item.Status));
            var hasGranted = validIpr.Any(item => IsGranted(item.Status));
            var state = hasFiled && hasGranted
                ? CompendiumIprVisualState.Mixed
                : hasGranted
                    ? CompendiumIprVisualState.Granted
                    : CompendiumIprVisualState.Filed;

            modules.Add(new CompendiumProgrammeModuleDto(
                CompendiumProgrammeModuleKind.Ipr,
                "IPR",
                BuildIprValue(validIpr),
                state switch
                {
                    CompendiumIprVisualState.Granted => "ipr-granted",
                    CompendiumIprVisualState.Mixed => "ipr-mixed",
                    _ => "ipr-filed"
                },
                "gold",
                state));
        }

        if (technologyTransfer is not null && NormalizeOptional(technologyTransfer.Status) is { } status)
        {
            var completionYear = status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                                 && technologyTransfer.CompletionYear.HasValue
                ? $" · {technologyTransfer.CompletionYear.Value}"
                : string.Empty;
            modules.Add(new CompendiumProgrammeModuleDto(
                CompendiumProgrammeModuleKind.TechnologyTransfer,
                "Technology transfer",
                $"{status}{completionYear}",
                "technology-transfer",
                "blue"));
        }

        return modules;
    }

    public static string BuildIprValue(IReadOnlyList<CompendiumIprCredentialDto> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var groups = credentials
            .Where(item => !string.IsNullOrWhiteSpace(item.Type) && (IsFiled(item.Status) || IsGranted(item.Status)))
            .GroupBy(item => item.Type.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var records = group.ToArray();
                var filed = records.Count(item => IsFiled(item.Status));
                var granted = records.Count(item => IsGranted(item.Status));
                var years = records
                    .Where(item => item.Year.HasValue)
                    .Select(item => item.Year!.Value)
                    .Distinct()
                    .OrderByDescending(year => year)
                    .Take(3)
                    .ToArray();
                var yearText = years.Length == 0 ? string.Empty : $" · {string.Join("/", years)}";

                if (filed > 0 && granted > 0)
                {
                    return $"{group.Key} · {FormatCount(granted, "granted")} · {FormatCount(filed, "filed")}{yearText}";
                }

                var status = granted > 0 ? "Granted" : "Filed";
                var countText = records.Length > 1 ? $" · {records.Length} records" : string.Empty;
                return $"{group.Key} · {status}{yearText}{countText}";
            })
            .ToArray();

        return groups.Length == 0 ? "Filed / Granted" : string.Join("\n", groups);
    }

    private static string FormatCount(int count, string status)
        => count == 1 ? $"1 {status}" : $"{count} {status}";

    private static bool IsFiled(string? status)
        => string.Equals(status?.Trim(), "Filed", StringComparison.OrdinalIgnoreCase);

    private static bool IsGranted(string? status)
        => string.Equals(status?.Trim(), "Granted", StringComparison.OrdinalIgnoreCase);

    private static bool IsNotRecorded(string value)
        => value.Equals("Not recorded", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
