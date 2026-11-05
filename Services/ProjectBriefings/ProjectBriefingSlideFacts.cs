using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

// What a generated deck said about one project: its stage, its external status remark and its
// costs, each reduced to a short comparable value. Kept with the deck on every generation so the
// library can tell, from the same resolvers the slides use, when the data has moved since.
public sealed record ProjectBriefingSlideFact(
    [property: JsonPropertyName("p")] int ProjectId,
    [property: JsonPropertyName("s")] string Stage,
    // Null when no slide showed this project's status remark (it is then not compared).
    [property: JsonPropertyName("r")] string? Status,
    [property: JsonPropertyName("c")] string Costs);

// The facts a slide would show for a project today, before the deck's layout decides which
// costs appear.
public sealed record ProjectBriefingCurrentProjectFacts(
    int ProjectId,
    string ProjectName,
    string StageCode,
    string? ExternalStatus,
    DateOnly? ExternalStatusDate,
    ProjectBriefingCostValue CostRd,
    ProjectBriefingCostValue IpaCost,
    ProjectBriefingCostValue ProliferationCost)
{
    public ProjectBriefingSlideFact ToSlideFact(ProjectBriefingLayout layout, ProjectBriefingCostMode costMode)
        => ProjectBriefingSlideFacts.Create(
            ProjectId,
            StageCode,
            ExternalStatus,
            ExternalStatusDate,
            CostRd,
            IpaCost,
            ProliferationCost,
            layout,
            costMode);
}

public sealed record ProjectBriefingMovedProject(int ProjectId, string ProjectName, bool Stage, bool Status, bool Cost);

// How the data behind a deck's projects moved since its last generation.
public sealed record ProjectBriefingDataMovement(int StageChanges, int StatusChanges, int CostChanges, IReadOnlyList<ProjectBriefingMovedProject> Projects)
{
    public static ProjectBriefingDataMovement None { get; } = new(0, 0, 0, Array.Empty<ProjectBriefingMovedProject>());

    public int Total => StageChanges + StatusChanges + CostChanges;

    public bool HasMoved => Total > 0;

    // "3 projects changed stage", "5 new status remarks", "1 cost revised".
    public IReadOnlyList<string> Describe()
    {
        var parts = new List<string>(3);
        if (StageChanges > 0)
        {
            parts.Add(StageChanges == 1 ? "1 project changed stage" : $"{StageChanges} projects changed stage");
        }

        if (StatusChanges > 0)
        {
            parts.Add(StatusChanges == 1 ? "1 new status remark" : $"{StatusChanges} new status remarks");
        }

        if (CostChanges > 0)
        {
            parts.Add(CostChanges == 1 ? "1 cost revised" : $"{CostChanges} costs revised");
        }

        return parts;
    }
}

public static class ProjectBriefingSlideFacts
{
    // The text a slide shows when a project has no external status; it is not a remark.
    public const string NoExternalStatus = "No external status recorded";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    // Which costs a deck draws, by the composer's rules (cost cards, cost table columns and the
    // portfolio cost metrics): the update sheet always shows the R&D cost (with its basis) and
    // totals the IPA cost; a standard briefing shows the R&D cost and/or the proliferation cost by
    // its cost mode, and never the IPA cost.
    public static (bool CostRd, bool Ipa, bool Proliferation) ShownCosts(ProjectBriefingLayout layout, ProjectBriefingCostMode costMode)
        => layout == ProjectBriefingLayout.ProjectUpdateSheet
            ? (true, true, false)
            : (costMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both,
                false,
                costMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both);

    public static ProjectBriefingSlideFact Create(
        int projectId,
        string? stageCode,
        string? externalStatus,
        DateOnly? externalStatusDate,
        ProjectBriefingCostValue? costRd,
        ProjectBriefingCostValue? ipaCost,
        ProjectBriefingCostValue? proliferationCost,
        ProjectBriefingLayout layout,
        ProjectBriefingCostMode costMode,
        bool statusShown = true)
    {
        var shown = ShownCosts(layout, costMode);
        var status = externalStatus?.Trim() ?? string.Empty;
        if (string.Equals(status, NoExternalStatus, StringComparison.Ordinal))
        {
            status = string.Empty;
        }

        return new ProjectBriefingSlideFact(
            projectId,
            (stageCode ?? string.Empty).Trim().ToUpperInvariant(),
            !statusShown
                ? null
                : status.Length == 0 && externalStatusDate is null
                ? string.Empty
                : $"{externalStatusDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{Hash(status)}",
            string.Join("|",
                shown.CostRd ? $"{Amount(costRd)}/{(costRd?.IsAvailable == true ? costRd.BasisDisplay : string.Empty)}" : string.Empty,
                shown.Ipa ? Amount(ipaCost) : string.Empty,
                shown.Proliferation ? Amount(proliferationCost) : string.Empty));
    }

    public static ProjectBriefingSlideFact FromPresentation(ProjectBriefingPresentationProject project, ProjectBriefingPresentationData data)
        => Create(
            project.ProjectId,
            project.PresentStageCode,
            project.ExternalStatus,
            project.ExternalStatusDate,
            project.CostRd,
            project.IpaCost,
            project.ProliferationCost,
            data.Layout,
            data.CostMode,
            IsStatusShown(project, data));

    // Where the composer draws a project's external status remark: every update sheet; the
    // executive table (Executive table and Combined modes, and the "other projects" table for
    // table-row-only projects); and a project's own slides only when "Show present status" is on.
    public static bool IsStatusShown(ProjectBriefingPresentationProject project, ProjectBriefingPresentationData data)
        => data.Layout == ProjectBriefingLayout.ProjectUpdateSheet
            || data.PresentationMode is ProjectBriefingPresentationMode.ExecutiveTable or ProjectBriefingPresentationMode.Combined
            || project.TableRowOnly
            || data.StandardSlideOptions.ShowPresentStatus;

    public static string Write(IEnumerable<ProjectBriefingSlideFact> facts)
        => JsonSerializer.Serialize(facts.OrderBy(fact => fact.ProjectId).ToArray(), JsonOptions);

    // Null when the deck was generated before facts were kept, or the stored value is unreadable:
    // the library then says nothing about movement rather than guessing.
    public static IReadOnlyList<ProjectBriefingSlideFact>? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectBriefingSlideFact[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Projects no longer resolvable today (deleted since) are left out: a removal is a deck edit,
    // not data that moved.
    public static ProjectBriefingDataMovement Compare(
        IReadOnlyList<ProjectBriefingSlideFact> generated,
        ProjectBriefingLayout layout,
        ProjectBriefingCostMode costMode,
        IReadOnlyDictionary<int, ProjectBriefingCurrentProjectFacts> current)
    {
        var moved = new List<ProjectBriefingMovedProject>();
        foreach (var before in generated)
        {
            if (!current.TryGetValue(before.ProjectId, out var facts))
            {
                continue;
            }

            var now = facts.ToSlideFact(layout, costMode);
            var stage = !string.Equals(before.Stage, now.Stage, StringComparison.Ordinal);
            var status = before.Status is not null && !string.Equals(before.Status, now.Status, StringComparison.Ordinal);
            var cost = !string.Equals(before.Costs, now.Costs, StringComparison.Ordinal);
            if (stage || status || cost)
            {
                moved.Add(new ProjectBriefingMovedProject(before.ProjectId, facts.ProjectName, stage, status, cost));
            }
        }

        return moved.Count == 0
            ? ProjectBriefingDataMovement.None
            : new ProjectBriefingDataMovement(
                moved.Count(project => project.Stage),
                moved.Count(project => project.Status),
                moved.Count(project => project.Cost),
                moved);
    }

    private static string Amount(ProjectBriefingCostValue? value)
        => value?.AmountInRupees is decimal amount && amount >= 0m
            ? decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    // A stable short digest (string.GetHashCode differs between processes).
    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 8);
}
