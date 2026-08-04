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

public sealed record ProjectBriefingDeckConfiguration(
    string? SelectionRulesJson,
    ProjectBriefingUpdateSheetOptions UpdateSheetOptions,
    ProjectBriefingStandardSlideOptions StandardSlideOptions,
    ProjectBriefingClosingSlideType ClosingSlideType);

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
            var updateSheetOptions = ReadUpdateSheetOptions(root["updateSheet"] as JsonObject);
            var standardSlideOptions = ReadStandardSlideOptions(root["standardBriefing"] as JsonObject);
            var closingSlideType = ReadClosingSlideType(root["closingSlide"]);

            return new ProjectBriefingDeckConfiguration(
                selectionRules,
                updateSheetOptions,
                standardSlideOptions,
                closingSlideType);
        }
        catch (JsonException)
        {
            // The legacy value should not block deck use. Treat it as opaque selection provenance.
            return Defaults(json);
        }
        catch (InvalidOperationException)
        {
            return Defaults(json);
        }
    }

    public static string WithSelectionRules(string? existingJson, string? selectionRulesJson)
    {
        var current = Read(existingJson);
        return Write(selectionRulesJson, current.UpdateSheetOptions, current.StandardSlideOptions, current.ClosingSlideType);
    }

    public static string WithUpdateSheetOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions options)
    {
        var current = Read(existingJson);
        return Write(current.SelectionRulesJson, options, current.StandardSlideOptions, current.ClosingSlideType);
    }

    public static string WithStandardSlideOptions(
        string? existingJson,
        ProjectBriefingStandardSlideOptions options)
    {
        var current = Read(existingJson);
        return Write(current.SelectionRulesJson, current.UpdateSheetOptions, options, current.ClosingSlideType);
    }

    public static string WithPresentationOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType)
    {
        var current = Read(existingJson);
        return Write(current.SelectionRulesJson, updateSheetOptions, standardSlideOptions, closingSlideType);
    }

    private static ProjectBriefingDeckConfiguration Defaults(string? selectionRulesJson)
        => new(
            selectionRulesJson,
            ProjectBriefingUpdateSheetOptions.Default,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingClosingSlideType.JaiHind);

    private static ProjectBriefingUpdateSheetOptions ReadUpdateSheetOptions(JsonObject? updateSheet)
    {
        var hideEmptyValues = updateSheet?["hideEmptyValues"]?.GetValue<bool>() ?? false;
        var rows = new List<ProjectBriefingUpdateSheetRow>();
        if (updateSheet?["rows"] is JsonArray rowArray)
        {
            foreach (var node in rowArray)
            {
                if (node is null) continue;
                if (node is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && Enum.TryParse<ProjectBriefingUpdateSheetRow>(text, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed))
                {
                    rows.Add(parsed);
                    continue;
                }

                if (node is JsonValue numericValue
                    && numericValue.TryGetValue<int>(out var number)
                    && Enum.IsDefined(typeof(ProjectBriefingUpdateSheetRow), number))
                {
                    rows.Add((ProjectBriefingUpdateSheetRow)number);
                }
            }
        }

        return ProjectBriefingUpdateSheetOptions.Normalize(rows, hideEmptyValues);
    }

    private static ProjectBriefingStandardSlideOptions ReadStandardSlideOptions(JsonObject? standard)
    {
        var layout = ProjectBriefingProjectBriefLayout.Automatic;
        var layoutNode = standard?["projectBriefLayout"];
        if (layoutNode is JsonValue layoutValue)
        {
            if (layoutValue.TryGetValue<string>(out var layoutText)
                && Enum.TryParse<ProjectBriefingProjectBriefLayout>(layoutText, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                layout = parsed;
            }
            else if (layoutValue.TryGetValue<int>(out var layoutNumber)
                     && Enum.IsDefined(typeof(ProjectBriefingProjectBriefLayout), layoutNumber))
            {
                layout = (ProjectBriefingProjectBriefLayout)layoutNumber;
            }
        }

        return ProjectBriefingStandardSlideOptions.Normalize(
            layout,
            standard?["showPresentStage"]?.GetValue<bool>() ?? true,
            standard?["showPresentStatus"]?.GetValue<bool>() ?? true);
    }


    private static ProjectBriefingClosingSlideType ReadClosingSlideType(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)
                && Enum.TryParse<ProjectBriefingClosingSlideType>(text, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                return parsed;
            }

            if (value.TryGetValue<int>(out var number)
                && Enum.IsDefined(typeof(ProjectBriefingClosingSlideType), number))
            {
                return (ProjectBriefingClosingSlideType)number;
            }
        }

        return ProjectBriefingClosingSlideType.JaiHind;
    }

    private static string Write(
        string? selectionRulesJson,
        ProjectBriefingUpdateSheetOptions updateSheetOptions,
        ProjectBriefingStandardSlideOptions standardSlideOptions,
        ProjectBriefingClosingSlideType closingSlideType)
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

        var root = new JsonObject
        {
            ["schema"] = Schema,
            ["selectionRules"] = ParseOptionalNode(selectionRulesJson),
            ["updateSheet"] = new JsonObject
            {
                ["rows"] = new JsonArray(normalizedUpdateSheet.Rows
                    .Select(row => (JsonNode?)JsonValue.Create(row.ToString()))
                    .ToArray()),
                ["hideEmptyValues"] = normalizedUpdateSheet.HideEmptyValues
            },
            ["standardBriefing"] = new JsonObject
            {
                ["projectBriefLayout"] = normalizedStandard.ProjectBriefLayout.ToString(),
                ["showPresentStage"] = normalizedStandard.ShowPresentStage,
                ["showPresentStatus"] = normalizedStandard.ShowPresentStatus
            },
            ["closingSlide"] = normalizedClosingSlideType.ToString()
        };

        return root.ToJsonString(JsonOptions);
    }

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
