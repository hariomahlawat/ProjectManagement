using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// User-configurable composition of a formal Project Update Sheet.
/// The project title and photograph/brief panels are structural and are not part of this row collection.
/// </summary>
public sealed record ProjectBriefingUpdateSheetOptions(
    IReadOnlyList<ProjectBriefingUpdateSheetRow> Rows,
    bool HideEmptyValues)
{
    public static IReadOnlyList<ProjectBriefingUpdateSheetRow> AllRows { get; } =
        new[]
        {
            ProjectBriefingUpdateSheetRow.ProjectCost,
            ProjectBriefingUpdateSheetRow.ArppPppNumber,
            ProjectBriefingUpdateSheetRow.FundingAuthority,
            ProjectBriefingUpdateSheetRow.AonDate,
            ProjectBriefingUpdateSheetRow.SupplyOrder,
            ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus,
            ProjectBriefingUpdateSheetRow.PresentStatus,
            ProjectBriefingUpdateSheetRow.ProjectOfficer,
            ProjectBriefingUpdateSheetRow.LineDirectorate
        };

    /// <summary>
    /// Compact command-update set used for new decks and by the Recommended defaults action.
    /// Optional ARPP/PPP, funding-authority and appointment rows remain available through AllRows.
    /// </summary>
    public static IReadOnlyList<ProjectBriefingUpdateSheetRow> RecommendedRows { get; } =
        new[]
        {
            ProjectBriefingUpdateSheetRow.ProjectCost,
            ProjectBriefingUpdateSheetRow.AonDate,
            ProjectBriefingUpdateSheetRow.SupplyOrder,
            ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus,
            ProjectBriefingUpdateSheetRow.PresentStatus
        };

    public static ProjectBriefingUpdateSheetOptions Default { get; } =
        new(RecommendedRows, HideEmptyValues: false);

    public static ProjectBriefingUpdateSheetOptions Normalize(
        IEnumerable<ProjectBriefingUpdateSheetRow>? rows,
        bool hideEmptyValues)
    {
        var normalized = (rows ?? Array.Empty<ProjectBriefingUpdateSheetRow>())
            .Where(Enum.IsDefined)
            .Distinct()
            .ToArray();

        return normalized.Length == 0
            ? Default with { HideEmptyValues = hideEmptyValues }
            : new ProjectBriefingUpdateSheetOptions(normalized, hideEmptyValues);
    }
}

/// <summary>
/// Detailed-slide preferences for the Standard PRISM Briefing template.
/// Cost remains governed independently by ProjectBriefingCostMode.
/// </summary>
public sealed record ProjectBriefingStandardSlideOptions(
    ProjectBriefingProjectBriefLayout ProjectBriefLayout,
    bool ShowPresentStage,
    bool ShowPresentStatus)
{
    public static ProjectBriefingStandardSlideOptions Default { get; } =
        new(
            ProjectBriefingProjectBriefLayout.Automatic,
            ShowPresentStage: true,
            ShowPresentStatus: true);

    public static ProjectBriefingStandardSlideOptions Normalize(
        ProjectBriefingProjectBriefLayout projectBriefLayout,
        bool showPresentStage,
        bool showPresentStatus)
        => new(
            Enum.IsDefined(projectBriefLayout)
                ? projectBriefLayout
                : ProjectBriefingProjectBriefLayout.Automatic,
            showPresentStage,
            showPresentStatus);
}

public sealed record ProjectBriefingInstitutionalHistoryMilestone(int Year, string Text);

/// <summary>
/// Deck-specific configuration of the optional SDD institutional-profile slide.
/// ERP-backed figures remain read-only; only authorised history, partnership and citation text is user-maintained.
/// </summary>
public sealed record ProjectBriefingInstitutionalProfileOptions(
    bool IncludeSlide,
    string Title,
    bool IncludeHistory,
    IReadOnlyList<ProjectBriefingInstitutionalHistoryMilestone> HistoryMilestones,
    IReadOnlyList<ProjectBriefingInstitutionalProfileModule> Modules,
    int MaximumDetailRows,
    string TrainingHighlightTechnicalCategory,
    IReadOnlyList<string> PartnershipEntries,
    bool IncludeUnitCitations,
    int? UnitCitationCount,
    string UnitCitationLabel)
{
    public const string DefaultTitle = "SDD – Growth over the years";
    public const string DefaultTrainingHighlight = "AR/VR";
    public const string DefaultCitationLabel = "GOC-in-C Unit Citations";

    public static IReadOnlyList<ProjectBriefingInstitutionalProfileModule> DefaultModules { get; } =
        new[]
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped,
            ProjectBriefingInstitutionalProfileModule.Proliferation,
            ProjectBriefingInstitutionalProfileModule.TrainingSupport,
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty,
            ProjectBriefingInstitutionalProfileModule.Partnerships
        };

    public static IReadOnlyList<ProjectBriefingInstitutionalHistoryMilestone> DefaultHistory { get; } =
        new[]
        {
            new ProjectBriefingInstitutionalHistoryMilestone(1986, "Conceptualised at MCEME"),
            new ProjectBriefingInstitutionalHistoryMilestone(1991, "Raising & 1st PE"),
            new ProjectBriefingInstitutionalHistoryMilestone(1998, "Established as CAT ‘A’"),
            new ProjectBriefingInstitutionalHistoryMilestone(2001, "KLP at present location"),
            new ProjectBriefingInstitutionalHistoryMilestone(2016, "AR/VR, AI & Robotics"),
            new ProjectBriefingInstitutionalHistoryMilestone(2024, "CoE (AR/VR)")
        };

    public static ProjectBriefingInstitutionalProfileOptions Default { get; } =
        new(
            IncludeSlide: false,
            Title: DefaultTitle,
            IncludeHistory: true,
            HistoryMilestones: DefaultHistory,
            Modules: DefaultModules,
            MaximumDetailRows: 6,
            TrainingHighlightTechnicalCategory: DefaultTrainingHighlight,
            PartnershipEntries: Array.Empty<string>(),
            IncludeUnitCitations: false,
            UnitCitationCount: null,
            UnitCitationLabel: DefaultCitationLabel);

    public static ProjectBriefingInstitutionalProfileOptions Normalize(
        bool includeSlide,
        string? title,
        bool includeHistory,
        IEnumerable<ProjectBriefingInstitutionalHistoryMilestone>? historyMilestones,
        IEnumerable<ProjectBriefingInstitutionalProfileModule>? modules,
        int maximumDetailRows,
        string? trainingHighlightTechnicalCategory,
        IEnumerable<string>? partnershipEntries,
        bool includeUnitCitations,
        int? unitCitationCount,
        string? unitCitationLabel)
    {
        var history = (historyMilestones ?? Array.Empty<ProjectBriefingInstitutionalHistoryMilestone>())
            .Where(item => item.Year is >= 1900 and <= 2200 && !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => new ProjectBriefingInstitutionalHistoryMilestone(
                item.Year,
                TrimTo(item.Text.Trim(), 100)))
            .Distinct()
            .Take(8)
            .ToArray();

        var normalizedModules = (modules ?? Array.Empty<ProjectBriefingInstitutionalProfileModule>())
            .Where(Enum.IsDefined)
            .Distinct()
            .Take(5)
            .ToArray();

        var normalizedPartnerships = (partnershipEntries ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TrimTo(value.Trim(), 80))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        var count = unitCitationCount is >= 0 and <= 999 ? unitCitationCount : null;

        return new ProjectBriefingInstitutionalProfileOptions(
            includeSlide,
            string.IsNullOrWhiteSpace(title) ? DefaultTitle : TrimTo(title.Trim(), 120),
            includeHistory,
            history.Length == 0 ? DefaultHistory : history,
            normalizedModules,
            Math.Clamp(maximumDetailRows, 3, 7),
            string.IsNullOrWhiteSpace(trainingHighlightTechnicalCategory)
                ? DefaultTrainingHighlight
                : TrimTo(trainingHighlightTechnicalCategory.Trim(), 80),
            normalizedPartnerships,
            includeUnitCitations && count.HasValue,
            count,
            string.IsNullOrWhiteSpace(unitCitationLabel)
                ? DefaultCitationLabel
                : TrimTo(unitCitationLabel.Trim(), 80));
    }

    private static string TrimTo(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength].TrimEnd();
}

public sealed record ProjectBriefingDeckConfiguration(
    string? SelectionRulesJson,
    ProjectBriefingUpdateSheetOptions UpdateSheetOptions,
    ProjectBriefingStandardSlideOptions StandardSlideOptions,
    ProjectBriefingClosingSlideType ClosingSlideType,
    ProjectBriefingInstitutionalProfileOptions InstitutionalProfileOptions);

/// <summary>
/// Stores selection provenance and presentation preferences in the existing JSONB deck configuration field.
/// Legacy selection-rule JSON remains readable and is upgraded only when settings are next saved.
/// </summary>
public static class ProjectBriefingDeckConfigurationCodec
{
    private const string Schema = "prism.projectBriefing.deckConfig.v1";

    public static ProjectBriefingDeckConfiguration Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Defaults(selectionRulesJson: null);
        }

        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null
                || !string.Equals(root["schema"]?.GetValue<string>(), Schema, StringComparison.Ordinal))
            {
                return Defaults(json);
            }

            var selectionRules = root["selectionRules"]?.ToJsonString(JsonOptions);
            return new ProjectBriefingDeckConfiguration(
                selectionRules,
                ReadUpdateSheetOptions(root["updateSheet"] as JsonObject),
                ReadStandardSlideOptions(root["standardBriefing"] as JsonObject),
                ReadClosingSlideType(root["closingSlide"]),
                ReadInstitutionalProfileOptions(root["institutionalProfile"] as JsonObject));
        }
        catch (JsonException)
        {
            return Defaults(json);
        }
        catch (InvalidOperationException)
        {
            return Defaults(json);
        }
        catch (FormatException)
        {
            return Defaults(json);
        }
    }

    public static string WithSelectionRules(string? existingJson, string? selectionRulesJson)
    {
        var current = Read(existingJson);
        return Write(
            selectionRulesJson,
            current.UpdateSheetOptions,
            current.StandardSlideOptions,
            current.ClosingSlideType,
            current.InstitutionalProfileOptions);
    }

    public static string WithUpdateSheetOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions)
    {
        var current = Read(existingJson);
        return Write(
            current.SelectionRulesJson,
            updateSheetOptions,
            current.StandardSlideOptions,
            current.ClosingSlideType,
            current.InstitutionalProfileOptions);
    }

    public static string WithStandardSlideOptions(
        string? existingJson,
        ProjectBriefingStandardSlideOptions standardSlideOptions)
    {
        var current = Read(existingJson);
        return Write(
            current.SelectionRulesJson,
            current.UpdateSheetOptions,
            standardSlideOptions,
            current.ClosingSlideType,
            current.InstitutionalProfileOptions);
    }

    public static string WithInstitutionalProfileOptions(
        string? existingJson,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions)
    {
        var current = Read(existingJson);
        return Write(
            current.SelectionRulesJson,
            current.UpdateSheetOptions,
            current.StandardSlideOptions,
            current.ClosingSlideType,
            institutionalProfileOptions);
    }

    public static string WithPresentationOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType)
    {
        var current = Read(existingJson);
        return WithPresentationOptions(
            existingJson,
            updateSheetOptions,
            standardSlideOptions,
            closingSlideType,
            current.InstitutionalProfileOptions);
    }

    public static string WithPresentationOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions)
    {
        var current = Read(existingJson);
        return Write(
            current.SelectionRulesJson,
            updateSheetOptions,
            standardSlideOptions,
            closingSlideType,
            institutionalProfileOptions);
    }

    private static ProjectBriefingDeckConfiguration Defaults(string? selectionRulesJson)
        => new(
            selectionRulesJson,
            ProjectBriefingUpdateSheetOptions.Default,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingClosingSlideType.JaiHind,
            ProjectBriefingInstitutionalProfileOptions.Default);

    private static ProjectBriefingUpdateSheetOptions ReadUpdateSheetOptions(JsonObject? updateSheet)
    {
        var hideEmptyValues = updateSheet?["hideEmptyValues"]?.GetValue<bool>() ?? false;
        return ProjectBriefingUpdateSheetOptions.Normalize(
            ReadEnumArray<ProjectBriefingUpdateSheetRow>(updateSheet?["rows"] as JsonArray),
            hideEmptyValues);
    }

    private static ProjectBriefingStandardSlideOptions ReadStandardSlideOptions(JsonObject? standard)
        => ProjectBriefingStandardSlideOptions.Normalize(
            ReadEnum(standard?["projectBriefLayout"], ProjectBriefingProjectBriefLayout.Automatic),
            standard?["showPresentStage"]?.GetValue<bool>() ?? true,
            standard?["showPresentStatus"]?.GetValue<bool>() ?? true);

    private static ProjectBriefingClosingSlideType ReadClosingSlideType(JsonNode? node)
        => ReadEnum(node, ProjectBriefingClosingSlideType.JaiHind);

    private static ProjectBriefingInstitutionalProfileOptions ReadInstitutionalProfileOptions(JsonObject? profile)
    {
        if (profile is null)
        {
            return ProjectBriefingInstitutionalProfileOptions.Default;
        }

        var history = new List<ProjectBriefingInstitutionalHistoryMilestone>();
        if (profile["history"] is JsonArray historyArray)
        {
            foreach (var item in historyArray.OfType<JsonObject>())
            {
                var year = item["year"]?.GetValue<int>() ?? 0;
                var text = item["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    history.Add(new ProjectBriefingInstitutionalHistoryMilestone(year, text));
                }
            }
        }

        var partnerships = profile["partnerships"] is JsonArray partnershipArray
            ? partnershipArray
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var text) ? text : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToArray()
            : Array.Empty<string>();

        return ProjectBriefingInstitutionalProfileOptions.Normalize(
            profile["includeSlide"]?.GetValue<bool>() ?? false,
            profile["title"]?.GetValue<string>(),
            profile["includeHistory"]?.GetValue<bool>() ?? true,
            history,
            profile["modules"] is JsonArray moduleArray
                ? ReadEnumArray<ProjectBriefingInstitutionalProfileModule>(moduleArray)
                : ProjectBriefingInstitutionalProfileOptions.DefaultModules,
            profile["maximumDetailRows"]?.GetValue<int>() ?? 6,
            profile["trainingHighlightTechnicalCategory"]?.GetValue<string>(),
            partnerships,
            profile["includeUnitCitations"]?.GetValue<bool>() ?? false,
            ReadNullableInt(profile["unitCitationCount"]),
            profile["unitCitationLabel"]?.GetValue<string>());
    }

    private static int? ReadNullableInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<int>(out var result) ? result : null;
    }

    private static string Write(
        string? selectionRulesJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions)
    {
        var normalizedUpdateSheet = ProjectBriefingUpdateSheetOptions.Normalize(
            updateSheetOptions.Rows,
            updateSheetOptions.HideEmptyValues);
        var normalizedStandard = ProjectBriefingStandardSlideOptions.Normalize(
            standardSlideOptions.ProjectBriefLayout,
            standardSlideOptions.ShowPresentStage,
            standardSlideOptions.ShowPresentStatus);
        var normalizedClosingSlideType = Enum.IsDefined(closingSlideType)
            ? closingSlideType
            : ProjectBriefingClosingSlideType.JaiHind;
        var normalizedProfile = ProjectBriefingInstitutionalProfileOptions.Normalize(
            institutionalProfileOptions.IncludeSlide,
            institutionalProfileOptions.Title,
            institutionalProfileOptions.IncludeHistory,
            institutionalProfileOptions.HistoryMilestones,
            institutionalProfileOptions.Modules,
            institutionalProfileOptions.MaximumDetailRows,
            institutionalProfileOptions.TrainingHighlightTechnicalCategory,
            institutionalProfileOptions.PartnershipEntries,
            institutionalProfileOptions.IncludeUnitCitations,
            institutionalProfileOptions.UnitCitationCount,
            institutionalProfileOptions.UnitCitationLabel);

        var root = new JsonObject
        {
            ["schema"] = Schema,
            ["selectionRules"] = ParseOptionalNode(selectionRulesJson),
            ["updateSheet"] = new JsonObject
            {
                ["rows"] = ToEnumArray(normalizedUpdateSheet.Rows),
                ["hideEmptyValues"] = normalizedUpdateSheet.HideEmptyValues
            },
            ["standardBriefing"] = new JsonObject
            {
                ["projectBriefLayout"] = normalizedStandard.ProjectBriefLayout.ToString(),
                ["showPresentStage"] = normalizedStandard.ShowPresentStage,
                ["showPresentStatus"] = normalizedStandard.ShowPresentStatus
            },
            ["closingSlide"] = normalizedClosingSlideType.ToString(),
            ["institutionalProfile"] = new JsonObject
            {
                ["includeSlide"] = normalizedProfile.IncludeSlide,
                ["title"] = normalizedProfile.Title,
                ["includeHistory"] = normalizedProfile.IncludeHistory,
                ["history"] = new JsonArray(normalizedProfile.HistoryMilestones
                    .Select(item => (JsonNode?)new JsonObject
                    {
                        ["year"] = item.Year,
                        ["text"] = item.Text
                    })
                    .ToArray()),
                ["modules"] = ToEnumArray(normalizedProfile.Modules),
                ["maximumDetailRows"] = normalizedProfile.MaximumDetailRows,
                ["trainingHighlightTechnicalCategory"] = normalizedProfile.TrainingHighlightTechnicalCategory,
                ["partnerships"] = new JsonArray(normalizedProfile.PartnershipEntries
                    .Select(item => (JsonNode?)JsonValue.Create(item))
                    .ToArray()),
                ["includeUnitCitations"] = normalizedProfile.IncludeUnitCitations,
                ["unitCitationCount"] = normalizedProfile.UnitCitationCount,
                ["unitCitationLabel"] = normalizedProfile.UnitCitationLabel
            }
        };

        return root.ToJsonString(JsonOptions);
    }

    private static TEnum ReadEnum<TEnum>(JsonNode? node, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)
                && Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                return parsed;
            }

            if (value.TryGetValue<int>(out var number)
                && Enum.IsDefined(typeof(TEnum), number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }
        }

        return fallback;
    }

    private static IReadOnlyList<TEnum> ReadEnumArray<TEnum>(JsonArray? array)
        where TEnum : struct, Enum
    {
        if (array is null)
        {
            return Array.Empty<TEnum>();
        }

        var values = new List<TEnum>();
        foreach (var node in array)
        {
            var parsed = ReadEnum<TEnum>(node, default);
            if (Enum.IsDefined(parsed))
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    private static JsonArray ToEnumArray<TEnum>(IEnumerable<TEnum> values)
        where TEnum : struct, Enum
        => new(values.Select(value => (JsonNode?)JsonValue.Create(value.ToString())).ToArray());

    private static JsonNode? ParseOptionalNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };
}
