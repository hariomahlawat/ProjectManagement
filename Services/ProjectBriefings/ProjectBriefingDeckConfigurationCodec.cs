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
/// ERP-backed figures remain read-only; only authorised history, partnership and optional footer-strip text is user-maintained.
/// </summary>
public sealed record ProjectBriefingInstitutionalProfileOptions(
    bool IncludeSlide,
    string Title,
    bool IncludeHistory,
    IReadOnlyList<ProjectBriefingInstitutionalHistoryMilestone> HistoryMilestones,
    IReadOnlyList<ProjectBriefingInstitutionalProfileModule> Modules,
    ProjectBriefingInstitutionalProjectScope ProjectScope,
    int MaximumDetailRows,
    string TrainingHighlightTechnicalCategory,
    IReadOnlyList<string> PartnershipEntries,
    bool IncludeFooterStrip,
    string FooterStripText,
    string? FooterStripEmphasisValue,
    ProjectBriefingInstitutionalFooterStyle FooterStripStyle,
    ProjectBriefingInstitutionalFooterAlignment FooterStripAlignment)
{
    public const string DefaultTitle = "SDD – Growth over the years";
    public const string DefaultTrainingHighlight = "AR/VR";
    public const string DefaultFooterText = "GOC-in-C Unit Citations";

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
            ProjectScope: ProjectBriefingInstitutionalProjectScope.OriginalCompleted,
            MaximumDetailRows: 6,
            TrainingHighlightTechnicalCategory: DefaultTrainingHighlight,
            PartnershipEntries: Array.Empty<string>(),
            IncludeFooterStrip: false,
            FooterStripText: DefaultFooterText,
            FooterStripEmphasisValue: null,
            FooterStripStyle: ProjectBriefingInstitutionalFooterStyle.Outline,
            FooterStripAlignment: ProjectBriefingInstitutionalFooterAlignment.Center);

    public static ProjectBriefingInstitutionalProfileOptions Normalize(
        bool includeSlide,
        string? title,
        bool includeHistory,
        IEnumerable<ProjectBriefingInstitutionalHistoryMilestone>? historyMilestones,
        IEnumerable<ProjectBriefingInstitutionalProfileModule>? modules,
        ProjectBriefingInstitutionalProjectScope projectScope,
        int maximumDetailRows,
        string? trainingHighlightTechnicalCategory,
        IEnumerable<string>? partnershipEntries,
        bool includeFooterStrip,
        string? footerStripText,
        string? footerStripEmphasisValue,
        ProjectBriefingInstitutionalFooterStyle footerStripStyle,
        ProjectBriefingInstitutionalFooterAlignment footerStripAlignment)
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
            .Take(6)
            .ToArray();

        var normalizedPartnerships = (partnershipEntries ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TrimTo(value.Trim(), 80))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        var normalizedFooterText = string.IsNullOrWhiteSpace(footerStripText)
            ? string.Empty
            : TrimTo(footerStripText.Trim(), 160);
        var normalizedFooterValue = string.IsNullOrWhiteSpace(footerStripEmphasisValue)
            ? null
            : TrimTo(footerStripEmphasisValue.Trim(), 40);
        var footerHasContent = !string.IsNullOrWhiteSpace(normalizedFooterText)
            || !string.IsNullOrWhiteSpace(normalizedFooterValue);

        return new ProjectBriefingInstitutionalProfileOptions(
            includeSlide,
            string.IsNullOrWhiteSpace(title) ? DefaultTitle : TrimTo(title.Trim(), 120),
            includeHistory,
            history.Length == 0 ? DefaultHistory : history,
            normalizedModules,
            Enum.IsDefined(projectScope)
                ? projectScope
                : ProjectBriefingInstitutionalProjectScope.OriginalCompleted,
            Math.Clamp(maximumDetailRows, 3, 7),
            string.IsNullOrWhiteSpace(trainingHighlightTechnicalCategory)
                ? DefaultTrainingHighlight
                : TrimTo(trainingHighlightTechnicalCategory.Trim(), 80),
            normalizedPartnerships,
            includeFooterStrip && footerHasContent,
            normalizedFooterText,
            normalizedFooterValue,
            Enum.IsDefined(footerStripStyle)
                ? footerStripStyle
                : ProjectBriefingInstitutionalFooterStyle.Outline,
            Enum.IsDefined(footerStripAlignment)
                ? footerStripAlignment
                : ProjectBriefingInstitutionalFooterAlignment.Center);
    }

    private static string TrimTo(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength].TrimEnd();
}

public sealed record ProjectBriefingRoleCharterEntry(string LeadPhrase, string Text);

/// <summary>
/// Deck-specific configuration of the optional Role &amp; Charter slide.
/// Shared authorised content is represented by the immutable defaults below; choosing deck
/// customisation creates an independent copy inside the existing versioned deck JSON.
/// </summary>
public sealed record ProjectBriefingRoleCharterOptions(
    bool IncludeSlide,
    string Title,
    ProjectBriefingRoleCharterLayout Layout,
    bool UseSharedContent,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> RoleStatements,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> CharterItems)
{
    public const string DefaultTitle = "Role & Charter";

    public static IReadOnlyList<ProjectBriefingRoleCharterEntry> SharedRoleStatements { get; } =
        new[]
        {
            new ProjectBriefingRoleCharterEntry(
                "Nodal Centre",
                "Development of specified simulators, robotics and AI products for the Indian Army"),
            new ProjectBriefingRoleCharterEntry(
                "Centre of Excellence",
                "AR/VR simulators")
        };

    public static IReadOnlyList<ProjectBriefingRoleCharterEntry> SharedCharterItems { get; } =
        new[]
        {
            new ProjectBriefingRoleCharterEntry("Repository", "Information related to simulators, AI and robotics"),
            new ProjectBriefingRoleCharterEntry("Facilitator", "QR, feasibility studies and scope of work"),
            new ProjectBriefingRoleCharterEntry("Procurement support", "On board for procurement of simulators, AI, VR/AR and robotics"),
            new ProjectBriefingRoleCharterEntry("Advisory role", "Development and production of simulators"),
            new ProjectBriefingRoleCharterEntry("Joint projects", "DRDO, PSUs and academia"),
            new ProjectBriefingRoleCharterEntry("Research and development", "Undertake R&D and experimental projects"),
            new ProjectBriefingRoleCharterEntry("Upgradation", "Upgrade existing projects and simulators"),
            new ProjectBriefingRoleCharterEntry("Development support", "Develop simulators and projects for FFCs"),
            new ProjectBriefingRoleCharterEntry("Professional engagement", "Participate in seminars, workshops, training and competitions"),
            new ProjectBriefingRoleCharterEntry("Coordination", "Coordinate with MGO Branch and EME Directorate on ESP issues")
        };

    public static ProjectBriefingRoleCharterOptions Default { get; } =
        new(
            IncludeSlide: false,
            Title: DefaultTitle,
            Layout: ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter,
            UseSharedContent: true,
            RoleStatements: SharedRoleStatements,
            CharterItems: SharedCharterItems);

    public static ProjectBriefingRoleCharterOptions Normalize(
        bool includeSlide,
        string? title,
        ProjectBriefingRoleCharterLayout layout,
        bool useSharedContent,
        IEnumerable<ProjectBriefingRoleCharterEntry>? roleStatements,
        IEnumerable<ProjectBriefingRoleCharterEntry>? charterItems)
    {
        var roles = useSharedContent
            ? SharedRoleStatements
            : NormalizeEntries(roleStatements, 4);
        var charter = useSharedContent
            ? SharedCharterItems
            : NormalizeEntries(charterItems, 18);

        return new ProjectBriefingRoleCharterOptions(
            includeSlide,
            string.IsNullOrWhiteSpace(title) ? DefaultTitle : TrimTo(title.Trim(), 120),
            Enum.IsDefined(layout) ? layout : ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter,
            useSharedContent,
            roles,
            charter);
    }

    private static IReadOnlyList<ProjectBriefingRoleCharterEntry> NormalizeEntries(
        IEnumerable<ProjectBriefingRoleCharterEntry>? source,
        int maximumCount)
        => (source ?? Array.Empty<ProjectBriefingRoleCharterEntry>())
            .Where(item => !string.IsNullOrWhiteSpace(item.LeadPhrase) || !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => new ProjectBriefingRoleCharterEntry(
                TrimTo((item.LeadPhrase ?? string.Empty).Trim(), 60),
                TrimTo((item.Text ?? string.Empty).Trim(), 240)))
            .Where(item => !string.IsNullOrWhiteSpace(item.LeadPhrase) || !string.IsNullOrWhiteSpace(item.Text))
            .Distinct()
            .Take(maximumCount)
            .ToArray();

    private static string TrimTo(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength].TrimEnd();
}

public sealed record ProjectBriefingDeckConfiguration(
    string? SelectionRulesJson,
    ProjectBriefingUpdateSheetOptions UpdateSheetOptions,
    ProjectBriefingStandardSlideOptions StandardSlideOptions,
    ProjectBriefingClosingSlideType ClosingSlideType,
    ProjectBriefingInstitutionalProfileOptions InstitutionalProfileOptions,
    ProjectBriefingRoleCharterOptions RoleCharterOptions,
    IReadOnlyList<ProjectBriefingAdditionalSlideType> AdditionalSlideOrder);

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
                ReadInstitutionalProfileOptions(root["institutionalProfile"] as JsonObject),
                ReadRoleCharterOptions(root["roleCharter"] as JsonObject),
                ReadAdditionalSlideOrder(root["additionalSlides"] as JsonArray));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return Defaults(json);
        }
    }

    public static string WithSelectionRules(string? existingJson, string? selectionRulesJson)
    {
        var current = Read(existingJson);
        return Write(current with { SelectionRulesJson = selectionRulesJson });
    }

    public static string WithUpdateSheetOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions)
    {
        var current = Read(existingJson);
        return Write(current with { UpdateSheetOptions = updateSheetOptions });
    }

    public static string WithStandardSlideOptions(
        string? existingJson,
        ProjectBriefingStandardSlideOptions standardSlideOptions)
    {
        var current = Read(existingJson);
        return Write(current with { StandardSlideOptions = standardSlideOptions });
    }

    public static string WithInstitutionalProfileOptions(
        string? existingJson,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions)
    {
        var current = Read(existingJson);
        return Write(current with { InstitutionalProfileOptions = institutionalProfileOptions });
    }

    public static string WithRoleCharterOptions(
        string? existingJson,
        ProjectBriefingRoleCharterOptions roleCharterOptions)
    {
        var current = Read(existingJson);
        return Write(current with { RoleCharterOptions = roleCharterOptions });
    }

    public static string WithAdditionalSlideOrder(
        string? existingJson,
        IEnumerable<ProjectBriefingAdditionalSlideType> order)
    {
        var current = Read(existingJson);
        return Write(current with { AdditionalSlideOrder = NormalizeAdditionalSlideOrder(order) });
    }

    public static string WithAdditionalSlides(
        string? existingJson,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions,
        ProjectBriefingRoleCharterOptions roleCharterOptions,
        IEnumerable<ProjectBriefingAdditionalSlideType> order)
    {
        var current = Read(existingJson);
        return Write(current with
        {
            InstitutionalProfileOptions = institutionalProfileOptions,
            RoleCharterOptions = roleCharterOptions,
            AdditionalSlideOrder = NormalizeAdditionalSlideOrder(order)
        });
    }

    public static string WithPresentationOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType)
    {
        var current = Read(existingJson);
        return Write(current with
        {
            UpdateSheetOptions = updateSheetOptions,
            StandardSlideOptions = standardSlideOptions,
            ClosingSlideType = closingSlideType
        });
    }

    public static string WithPresentationOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType,
        ProjectBriefingInstitutionalProfileOptions institutionalProfileOptions)
    {
        var current = Read(existingJson);
        return Write(current with
        {
            UpdateSheetOptions = updateSheetOptions,
            StandardSlideOptions = standardSlideOptions,
            ClosingSlideType = closingSlideType,
            InstitutionalProfileOptions = institutionalProfileOptions
        });
    }

    private static ProjectBriefingDeckConfiguration Defaults(string? selectionRulesJson)
        => new(
            selectionRulesJson,
            ProjectBriefingUpdateSheetOptions.Default,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingClosingSlideType.JaiHind,
            ProjectBriefingInstitutionalProfileOptions.Default,
            ProjectBriefingRoleCharterOptions.Default,
            new[] { ProjectBriefingAdditionalSlideType.InstitutionalProfile });

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

        var hasNewFooterConfiguration = profile.ContainsKey("includeFooterStrip")
            || profile.ContainsKey("footerStripText")
            || profile.ContainsKey("footerStripEmphasisValue");
        var legacyCitationCount = ReadNullableInt(profile["unitCitationCount"]);
        var footerEnabled = hasNewFooterConfiguration
            ? profile["includeFooterStrip"]?.GetValue<bool>() ?? false
            : profile["includeUnitCitations"]?.GetValue<bool>() ?? false;
        var footerText = hasNewFooterConfiguration
            ? profile["footerStripText"]?.GetValue<string>()
            : profile["unitCitationLabel"]?.GetValue<string>();
        var footerValue = hasNewFooterConfiguration
            ? profile["footerStripEmphasisValue"]?.GetValue<string>()
            : legacyCitationCount.HasValue
                ? legacyCitationCount.Value.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
                : null;

        return ProjectBriefingInstitutionalProfileOptions.Normalize(
            profile["includeSlide"]?.GetValue<bool>() ?? false,
            profile["title"]?.GetValue<string>(),
            profile["includeHistory"]?.GetValue<bool>() ?? true,
            history,
            profile["modules"] is JsonArray moduleArray
                ? ReadEnumArray<ProjectBriefingInstitutionalProfileModule>(moduleArray)
                : ProjectBriefingInstitutionalProfileOptions.DefaultModules,
            ReadEnum(profile["projectScope"], ProjectBriefingInstitutionalProjectScope.OriginalCompleted),
            profile["maximumDetailRows"]?.GetValue<int>() ?? 6,
            profile["trainingHighlightTechnicalCategory"]?.GetValue<string>(),
            partnerships,
            footerEnabled,
            footerText,
            footerValue,
            ReadEnum(profile["footerStripStyle"], ProjectBriefingInstitutionalFooterStyle.Outline),
            ReadEnum(profile["footerStripAlignment"], ProjectBriefingInstitutionalFooterAlignment.Center));
    }

    private static ProjectBriefingRoleCharterOptions ReadRoleCharterOptions(JsonObject? roleCharter)
    {
        if (roleCharter is null)
        {
            return ProjectBriefingRoleCharterOptions.Default;
        }

        return ProjectBriefingRoleCharterOptions.Normalize(
            roleCharter["includeSlide"]?.GetValue<bool>() ?? false,
            roleCharter["title"]?.GetValue<string>(),
            ReadEnum(roleCharter["layout"], ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter),
            roleCharter["useSharedContent"]?.GetValue<bool>() ?? true,
            ReadRoleCharterEntries(roleCharter["roleStatements"] as JsonArray),
            ReadRoleCharterEntries(roleCharter["charterItems"] as JsonArray));
    }

    private static IReadOnlyList<ProjectBriefingRoleCharterEntry> ReadRoleCharterEntries(JsonArray? array)
        => array is null
            ? Array.Empty<ProjectBriefingRoleCharterEntry>()
            : array.OfType<JsonObject>()
                .Select(item => new ProjectBriefingRoleCharterEntry(
                    item["leadPhrase"]?.GetValue<string>() ?? string.Empty,
                    item["text"]?.GetValue<string>() ?? string.Empty))
                .ToArray();

    private static IReadOnlyList<ProjectBriefingAdditionalSlideType> ReadAdditionalSlideOrder(JsonArray? array)
    {
        // Older deck JSON did not contain an additionalSlides node. Preserve the
        // original SDD-profile behaviour only for that legacy case. An explicitly
        // stored empty array means the user has removed every optional slide.
        if (array is null)
        {
            return new[] { ProjectBriefingAdditionalSlideType.InstitutionalProfile };
        }

        return NormalizeAdditionalSlideOrder(ReadEnumArray<ProjectBriefingAdditionalSlideType>(array));
    }

    public static IReadOnlyList<ProjectBriefingAdditionalSlideType> NormalizeAdditionalSlideOrder(
        IEnumerable<ProjectBriefingAdditionalSlideType>? order)
        => (order ?? Array.Empty<ProjectBriefingAdditionalSlideType>())
            .Where(ProjectBriefingAdditionalSlideCatalog.IsRegistered)
            .Distinct()
            .Take(8)
            .ToArray();

    private static int? ReadNullableInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<int>(out var result) ? result : null;
    }

    private static string Write(ProjectBriefingDeckConfiguration configuration)
    {
        var normalizedUpdateSheet = ProjectBriefingUpdateSheetOptions.Normalize(
            configuration.UpdateSheetOptions.Rows,
            configuration.UpdateSheetOptions.HideEmptyValues);
        var normalizedStandard = ProjectBriefingStandardSlideOptions.Normalize(
            configuration.StandardSlideOptions.ProjectBriefLayout,
            configuration.StandardSlideOptions.ShowPresentStage,
            configuration.StandardSlideOptions.ShowPresentStatus);
        var normalizedClosingSlideType = Enum.IsDefined(configuration.ClosingSlideType)
            ? configuration.ClosingSlideType
            : ProjectBriefingClosingSlideType.JaiHind;
        var profile = configuration.InstitutionalProfileOptions;
        var normalizedProfile = ProjectBriefingInstitutionalProfileOptions.Normalize(
            profile.IncludeSlide,
            profile.Title,
            profile.IncludeHistory,
            profile.HistoryMilestones,
            profile.Modules,
            profile.ProjectScope,
            profile.MaximumDetailRows,
            profile.TrainingHighlightTechnicalCategory,
            profile.PartnershipEntries,
            profile.IncludeFooterStrip,
            profile.FooterStripText,
            profile.FooterStripEmphasisValue,
            profile.FooterStripStyle,
            profile.FooterStripAlignment);
        var roleCharter = configuration.RoleCharterOptions;
        var normalizedRoleCharter = ProjectBriefingRoleCharterOptions.Normalize(
            roleCharter.IncludeSlide,
            roleCharter.Title,
            roleCharter.Layout,
            roleCharter.UseSharedContent,
            roleCharter.RoleStatements,
            roleCharter.CharterItems);
        var normalizedAdditionalSlideOrder = NormalizeAdditionalSlideOrder(configuration.AdditionalSlideOrder);

        var root = new JsonObject
        {
            ["schema"] = Schema,
            ["selectionRules"] = ParseOptionalNode(configuration.SelectionRulesJson),
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
                ["projectScope"] = normalizedProfile.ProjectScope.ToString(),
                ["maximumDetailRows"] = normalizedProfile.MaximumDetailRows,
                ["trainingHighlightTechnicalCategory"] = normalizedProfile.TrainingHighlightTechnicalCategory,
                ["partnerships"] = new JsonArray(normalizedProfile.PartnershipEntries
                    .Select(item => (JsonNode?)JsonValue.Create(item))
                    .ToArray()),
                ["includeFooterStrip"] = normalizedProfile.IncludeFooterStrip,
                ["footerStripText"] = normalizedProfile.FooterStripText,
                ["footerStripEmphasisValue"] = normalizedProfile.FooterStripEmphasisValue,
                ["footerStripStyle"] = normalizedProfile.FooterStripStyle.ToString(),
                ["footerStripAlignment"] = normalizedProfile.FooterStripAlignment.ToString()
            },
            ["roleCharter"] = new JsonObject
            {
                ["includeSlide"] = normalizedRoleCharter.IncludeSlide,
                ["title"] = normalizedRoleCharter.Title,
                ["layout"] = normalizedRoleCharter.Layout.ToString(),
                ["useSharedContent"] = normalizedRoleCharter.UseSharedContent,
                ["roleStatements"] = ToRoleCharterEntryArray(normalizedRoleCharter.RoleStatements),
                ["charterItems"] = ToRoleCharterEntryArray(normalizedRoleCharter.CharterItems)
            },
            ["additionalSlides"] = ToEnumArray(normalizedAdditionalSlideOrder)
        };

        return root.ToJsonString(JsonOptions);
    }

    private static JsonArray ToRoleCharterEntryArray(IEnumerable<ProjectBriefingRoleCharterEntry> entries)
        => new(entries.Select(item => (JsonNode?)new JsonObject
        {
            ["leadPhrase"] = item.LeadPhrase,
            ["text"] = item.Text
        }).ToArray());

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
