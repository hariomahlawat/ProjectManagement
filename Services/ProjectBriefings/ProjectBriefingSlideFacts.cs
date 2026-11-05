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
    [property: JsonPropertyName("r")] string Status,
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
    // The update sheet never shows a proliferation cost (the data service does not resolve one).
    public ProjectBriefingSlideFact ToSlideFact(ProjectBriefingLayout layout)
        => ProjectBriefingSlideFacts.Create(
            ProjectId,
            StageCode,
            ExternalStatus,
            ExternalStatusDate,
            CostRd,
            IpaCost,
            layout == ProjectBriefingLayout.ProjectUpdateSheet
                ? ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation)
                : ProliferationCost);
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

    public static ProjectBriefingSlideFact Create(
        int projectId,
        string? stageCode,
        string? externalStatus,
        DateOnly? externalStatusDate,
        ProjectBriefingCostValue? costRd,
        ProjectBriefingCostValue? ipaCost,
        ProjectBriefingCostValue? proliferationCost)
    {
        var status = externalStatus?.Trim() ?? string.Empty;
        if (string.Equals(status, NoExternalStatus, StringComparison.Ordinal))
        {
            status = string.Empty;
        }

        return new ProjectBriefingSlideFact(
            projectId,
            (stageCode ?? string.Empty).Trim().ToUpperInvariant(),
            status.Length == 0 && externalStatusDate is null
                ? string.Empty
                : $"{externalStatusDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{Hash(status)}",
            $"{Amount(costRd)}|{Amount(ipaCost)}|{Amount(proliferationCost)}");
    }

    public static ProjectBriefingSlideFact FromPresentation(ProjectBriefingPresentationProject project)
        => Create(
            project.ProjectId,
            project.PresentStageCode,
            project.ExternalStatus,
            project.ExternalStatusDate,
            project.CostRd,
            project.IpaCost,
            project.ProliferationCost);

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
        IReadOnlyDictionary<int, ProjectBriefingCurrentProjectFacts> current)
    {
        var moved = new List<ProjectBriefingMovedProject>();
        foreach (var before in generated)
        {
            if (!current.TryGetValue(before.ProjectId, out var facts))
            {
                continue;
            }

            var now = facts.ToSlideFact(layout);
            var stage = !string.Equals(before.Stage, now.Stage, StringComparison.Ordinal);
            var status = !string.Equals(before.Status, now.Status, StringComparison.Ordinal);
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
