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
    public static IReadOnlyList<ProjectBriefingUpdateSheetRow> RecommendedRows { get; } =
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

public sealed record ProjectBriefingDeckConfiguration(
    string? SelectionRulesJson,
    ProjectBriefingUpdateSheetOptions UpdateSheetOptions);

/// <summary>
/// Stores selection provenance and update-sheet preferences in the existing JSONB deck configuration field.
/// Legacy selection-rule JSON remains readable and is upgraded only when settings are next saved.
/// </summary>
public static class ProjectBriefingDeckConfigurationCodec
{
    private const string Schema = "prism.projectBriefing.deckConfig.v1";

    public static ProjectBriefingDeckConfiguration Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProjectBriefingDeckConfiguration(null, ProjectBriefingUpdateSheetOptions.Default);
        }

        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null
                || !string.Equals(root["schema"]?.GetValue<string>(), Schema, StringComparison.Ordinal))
            {
                return new ProjectBriefingDeckConfiguration(json, ProjectBriefingUpdateSheetOptions.Default);
            }

            var selectionRules = root["selectionRules"]?.ToJsonString(JsonOptions);
            var updateSheet = root["updateSheet"] as JsonObject;
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

            return new ProjectBriefingDeckConfiguration(
                selectionRules,
                ProjectBriefingUpdateSheetOptions.Normalize(rows, hideEmptyValues));
        }
        catch (JsonException)
        {
            // The legacy value should not block deck use. Treat it as opaque selection provenance.
            return new ProjectBriefingDeckConfiguration(json, ProjectBriefingUpdateSheetOptions.Default);
        }
        catch (InvalidOperationException)
        {
            return new ProjectBriefingDeckConfiguration(json, ProjectBriefingUpdateSheetOptions.Default);
        }
    }

    public static string WithSelectionRules(string? existingJson, string? selectionRulesJson)
    {
        var current = Read(existingJson);
        return Write(selectionRulesJson, current.UpdateSheetOptions);
    }

    public static string WithUpdateSheetOptions(
        string? existingJson,
        ProjectBriefingUpdateSheetOptions options)
    {
        var current = Read(existingJson);
        return Write(current.SelectionRulesJson, options);
    }

    private static string Write(
        string? selectionRulesJson,
        ProjectBriefingUpdateSheetOptions options)
    {
        var normalized = ProjectBriefingUpdateSheetOptions.Normalize(options.Rows, options.HideEmptyValues);
        var root = new JsonObject
        {
            ["schema"] = Schema,
            ["selectionRules"] = ParseOptionalNode(selectionRulesJson),
            ["updateSheet"] = new JsonObject
            {
                ["rows"] = new JsonArray(normalized.Rows
                    .Select(row => (JsonNode?)JsonValue.Create(row.ToString()))
                    .ToArray()),
                ["hideEmptyValues"] = normalized.HideEmptyValues
            }
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
