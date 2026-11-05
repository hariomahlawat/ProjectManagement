using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings.Presentation;

namespace ProjectManagement.Services.ProjectBriefings;

// Which of a project's facts a generated deck drew, and so which are compared afterwards.
[Flags]
public enum ProjectBriefingShownFacts
{
    None = 0,
    // The stage name is drawn (a table row, a detail slide, the stage summary).
    Stage = 1,
    // The stage's maturity orders two or more projects' slides.
    StageOrder = 2,
    Status = 4,
    CostRd = 8,
    IpaCost = 16,
    ProliferationCost = 32,
    // The lifecycle (ongoing, completed, cancelled): a standard detail slide's subtitle, the
    // portfolio summary's counts, and the stage summary's status band and ongoing-stage chart.
    Lifecycle = 64,
    // An update sheet's milestone row: a PDC date in development, else completion or cancellation.
    Milestone = 128,
    // The R&D cost's basis label ("PNC", "L1"): standard cost cards and the executive table draw
    // it; update-sheet rows and the portfolio totals show the amount only.
    CostRdBasis = 256,
    // The project's key points, on the slide a deck that shows key points chose.
    KeyPoints = 512
}

// What a generated deck said about one project, each fact reduced to a short comparable value,
// with the facts it showed. Kept with the deck on every generation so the library can tell, from
// the same resolvers the slides use, when data the slides showed has moved since. A fact the deck
// did not show is null and never compared.
public sealed record ProjectBriefingSlideFact(
    [property: JsonPropertyName("p")] int ProjectId,
    [property: JsonPropertyName("v")] ProjectBriefingShownFacts Shown,
    [property: JsonPropertyName("s")] string? Stage,
    [property: JsonPropertyName("r")] string? Status,
    [property: JsonPropertyName("c")] string? Costs,
    // A digest of the key points drawn (empty when none were recorded); null when not shown, and in
    // facts kept before key points existed.
    [property: JsonPropertyName("k")] string? KeyPoints = null);

// The facts a slide would show for a project today.
public sealed record ProjectBriefingCurrentProjectFacts(
    int ProjectId,
    string ProjectName,
    ProjectLifecycleStatus LifecycleStatus,
    string StageCode,
    string? ExternalStatus,
    DateOnly? ExternalStatusDate,
    ProjectBriefingCostValue CostRd,
    ProjectBriefingCostValue IpaCost,
    ProjectBriefingCostValue ProliferationCost,
    // For an update sheet's milestone row (resolved only where a generation showed that row).
    DateOnly? DevelopmentPdcDate = null,
    string? CompletionStatusDisplay = null,
    // The project's recorded key points, in catalogue order.
    IReadOnlyList<ProjectBriefingKeyPoint>? KeyPoints = null)
{
    // Today's facts, reduced the way a generation that showed `shown` reduced them.
    public ProjectBriefingSlideFact ToSlideFact(ProjectBriefingShownFacts shown)
        => ProjectBriefingSlideFacts.Create(
            ProjectId,
            shown,
            StageCode,
            ProjectBriefingStageOrder.Resolve(LifecycleStatus, StageCode),
            LifecycleStatus,
            ExternalStatus,
            ExternalStatusDate,
            CostRd,
            IpaCost,
            ProliferationCost,
            DevelopmentPdcDate,
            CompletionStatusDisplay,
            KeyPoints);
}

// The facts a generation kept, with the layout and cost mode it was drawn with.
public sealed record ProjectBriefingGeneratedFacts(
    [property: JsonPropertyName("l")] ProjectBriefingLayout Layout,
    [property: JsonPropertyName("m")] ProjectBriefingCostMode CostMode,
    [property: JsonPropertyName("f")] IReadOnlyList<ProjectBriefingSlideFact> Facts);

public sealed record ProjectBriefingMovedProject(int ProjectId, string ProjectName, bool Stage, bool Status, bool Cost, bool KeyPoints = false);

// How the data behind a deck's projects moved since its last generation.
public sealed record ProjectBriefingDataMovement(
    int StageChanges,
    int StatusChanges,
    int CostChanges,
    IReadOnlyList<ProjectBriefingMovedProject> Projects,
    int KeyPointChanges = 0)
{
    public static ProjectBriefingDataMovement None { get; } = new(0, 0, 0, Array.Empty<ProjectBriefingMovedProject>());

    public int Total => StageChanges + StatusChanges + CostChanges + KeyPointChanges;

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

        if (KeyPointChanges > 0)
        {
            parts.Add(KeyPointChanges == 1 ? "key points edited on 1 project" : $"key points edited on {KeyPointChanges} projects");
        }

        return parts;
    }
}

public static class ProjectBriefingSlideFacts
{
    // The text a slide shows when a project has no external status; it is not a remark.
    public const string NoExternalStatus = "No external status recorded";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    // Which facts the composer draws for this project in this deck (ProjectBriefingSlideComposer):
    // - the executive table (table modes; a table-row-only project in any mode) draws stage,
    //   status and the costs of the cost mode;
    // - a standard detail slide draws stage and status when "Show present stage/status" is on,
    //   and the cost cards of the cost mode;
    // - an update sheet draws its selected rows: cost, status, and the milestone row (a PDC date in
    //   development, else the completion or cancellation; never the stage name);
    // - a standard detail slide's subtitle draws the lifecycle ("Ongoing", "Cancelled", ...);
    // - a deck that shows key points draws them on the slide it chose (ProjectBriefingSlideComposer.KeyPointSlide);
    // - the stage summary draws every project's stage; the portfolio summary counts lifecycle
    //   (ongoing, completed, cancelled) and totals the costs (the IPA cost only on update sheets);
    // - with two or more projects on slides of their own, stage maturity orders the slides.
    public static ProjectBriefingShownFacts Shown(ProjectBriefingPresentationProject project, ProjectBriefingPresentationData data)
    {
        var updateSheet = data.Layout == ProjectBriefingLayout.ProjectUpdateSheet;
        var costMode = updateSheet ? ProjectBriefingCostMode.CostRdOnly : data.CostMode;
        var costRdMode = costMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both;
        var proliferationMode = costMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both;
        var inTable = project.TableRowOnly
            || (!updateSheet && data.PresentationMode is ProjectBriefingPresentationMode.ExecutiveTable or ProjectBriefingPresentationMode.Combined);
        var hasOwnSlides = !project.TableRowOnly
            && (updateSheet || data.PresentationMode is ProjectBriefingPresentationMode.DetailedProjects or ProjectBriefingPresentationMode.Combined);
        var rows = data.UpdateSheetOptions.Rows;
        var shown = ProjectBriefingShownFacts.None;

        if (inTable
            || (hasOwnSlides && !updateSheet && data.StandardSlideOptions.ShowPresentStage)
            || (!updateSheet && data.IncludeStageSummary))
        {
            shown |= ProjectBriefingShownFacts.Stage;
        }
        else if (data.Projects.Count(other => !other.TableRowOnly) >= 2)
        {
            shown |= ProjectBriefingShownFacts.StageOrder;
        }

        // A standard project's subtitle ("Ongoing · ...") is drawn on its capability slides and on
        // continuations (in the key points layout, on a first capability slide only when it has no
        // stage and cost line), not on the first project-brief slide.
        var drawsSubtitle = hasOwnSlides && !updateSheet
            && ProjectBriefingSlideComposer.DrawsLifecycleSubtitle(data, project);
        if (data.IncludePortfolioSummarySlide
            || drawsSubtitle
            || (!updateSheet && data.IncludeStageSummary))
        {
            shown |= ProjectBriefingShownFacts.Lifecycle;
        }

        if (hasOwnSlides && updateSheet && rows.Contains(ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus))
        {
            shown |= ProjectBriefingShownFacts.Milestone;
        }

        if (inTable
            || (hasOwnSlides && (updateSheet
                ? rows.Contains(ProjectBriefingUpdateSheetRow.PresentStatus)
                : data.StandardSlideOptions.ShowPresentStatus)))
        {
            shown |= ProjectBriefingShownFacts.Status;
        }

        if (costRdMode
            && (inTable
                || data.IncludePortfolioSummarySlide
                || (hasOwnSlides && (!updateSheet || rows.Contains(ProjectBriefingUpdateSheetRow.ProjectCost)))))
        {
            shown |= ProjectBriefingShownFacts.CostRd;
            if (inTable || (hasOwnSlides && !updateSheet))
            {
                shown |= ProjectBriefingShownFacts.CostRdBasis;
            }
        }

        if (updateSheet && data.IncludePortfolioSummarySlide)
        {
            shown |= ProjectBriefingShownFacts.IpaCost;
        }

        if (proliferationMode && (inTable || hasOwnSlides || data.IncludePortfolioSummarySlide))
        {
            shown |= ProjectBriefingShownFacts.ProliferationCost;
        }

        if (hasOwnSlides && !updateSheet && ProjectBriefingSlideComposer.KeyPointSlide(data) is not null)
        {
            shown |= ProjectBriefingShownFacts.KeyPoints;
        }

        return shown;
    }

    public static ProjectBriefingSlideFact Create(
        int projectId,
        ProjectBriefingShownFacts shown,
        string? stageCode,
        int stageOrder,
        ProjectLifecycleStatus lifecycleStatus,
        string? externalStatus,
        DateOnly? externalStatusDate,
        ProjectBriefingCostValue? costRd,
        ProjectBriefingCostValue? ipaCost,
        ProjectBriefingCostValue? proliferationCost,
        DateOnly? developmentPdcDate = null,
        string? completionStatusDisplay = null,
        IReadOnlyList<ProjectBriefingKeyPoint>? keyPoints = null)
    {
        var status = externalStatus?.Trim() ?? string.Empty;
        if (string.Equals(status, NoExternalStatus, StringComparison.Ordinal))
        {
            status = string.Empty;
        }

        var showsCost = (shown & (ProjectBriefingShownFacts.CostRd | ProjectBriefingShownFacts.IpaCost | ProjectBriefingShownFacts.ProliferationCost)) != 0;
        var code = (stageCode ?? string.Empty).Trim().ToUpperInvariant();

        // Each drawn reading of the stage, as one comparable value (null when none is drawn).
        var stageParts = new List<string>(4);
        if (shown.HasFlag(ProjectBriefingShownFacts.Stage))
        {
            stageParts.Add(code);
        }

        if (shown.HasFlag(ProjectBriefingShownFacts.StageOrder))
        {
            stageParts.Add($"#{stageOrder.ToString(CultureInfo.InvariantCulture)}");
        }

        if (shown.HasFlag(ProjectBriefingShownFacts.Lifecycle))
        {
            stageParts.Add($"L{lifecycleStatus}");
        }

        if (shown.HasFlag(ProjectBriefingShownFacts.Milestone))
        {
            // The row exactly as the update sheet draws it (the planner's own rule).
            stageParts.Add("M:" + ProjectBriefingUpdateSheetPlanner.DescribeMilestone(
                lifecycleStatus, code, developmentPdcDate, completionStatusDisplay));
        }

        return new ProjectBriefingSlideFact(
            projectId,
            shown,
            stageParts.Count == 0 ? null : string.Join("|", stageParts),
            !shown.HasFlag(ProjectBriefingShownFacts.Status)
                ? null
                : status.Length == 0 && externalStatusDate is null
                    ? string.Empty
                    : $"{externalStatusDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{Hash(status)}",
            !showsCost
                ? null
                : string.Join("|",
                    shown.HasFlag(ProjectBriefingShownFacts.CostRd)
                        ? shown.HasFlag(ProjectBriefingShownFacts.CostRdBasis)
                            ? $"{Amount(costRd)}/{(costRd?.IsAvailable == true ? costRd.BasisDisplay : string.Empty)}"
                            : Amount(costRd)
                        : string.Empty,
                    shown.HasFlag(ProjectBriefingShownFacts.IpaCost) ? Amount(ipaCost) : string.Empty,
                    shown.HasFlag(ProjectBriefingShownFacts.ProliferationCost) ? Amount(proliferationCost) : string.Empty),
            shown.HasFlag(ProjectBriefingShownFacts.KeyPoints) ? KeyPointDigest(keyPoints) : null);
    }

    // Every drawn point under its heading; empty when the project has none.
    private static string KeyPointDigest(IReadOnlyList<ProjectBriefingKeyPoint>? keyPoints)
    {
        var points = (keyPoints ?? Array.Empty<ProjectBriefingKeyPoint>())
            .Where(point => !string.IsNullOrWhiteSpace(point.Text))
            .Select(point => $"{point.Heading}={point.Text.Trim()}")
            .ToArray();
        return points.Length == 0 ? string.Empty : Hash(string.Join("\n", points));
    }

    public static ProjectBriefingSlideFact FromPresentation(ProjectBriefingPresentationProject project, ProjectBriefingPresentationData data)
        => Create(
            project.ProjectId,
            Shown(project, data),
            project.PresentStageCode,
            project.PresentStageOrder,
            project.LifecycleStatus,
            project.ExternalStatus,
            project.ExternalStatusDate,
            project.CostRd,
            project.IpaCost,
            project.ProliferationCost,
            project.DevelopmentPdcDate,
            project.CompletionStatusDisplay,
            project.KeyPoints);

    public static string Write(ProjectBriefingPresentationData data)
        => Write(data.Layout, data.CostMode, data.Projects.Select(project => FromPresentation(project, data)));

    public static string Write(ProjectBriefingLayout layout, ProjectBriefingCostMode costMode, IEnumerable<ProjectBriefingSlideFact> facts)
        => JsonSerializer.Serialize(
            new ProjectBriefingGeneratedFacts(layout, costMode, facts.OrderBy(fact => fact.ProjectId).ToArray()),
            JsonOptions);

    // Null when the deck was generated before facts were kept, or the stored value is unreadable:
    // the library then says nothing about movement rather than guessing.
    public static ProjectBriefingGeneratedFacts? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectBriefingGeneratedFacts>(json, JsonOptions) is { Facts: not null } read
                ? read
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Today's facts are reduced with what each project's generated slides showed, so a fact the
    // deck did not show, or a settings change since, never counts. Projects no longer resolvable
    // today (deleted since) are left out: a removal is a deck edit, not data that moved.
    public static ProjectBriefingDataMovement Compare(
        ProjectBriefingGeneratedFacts generated,
        IReadOnlyDictionary<int, ProjectBriefingCurrentProjectFacts> current)
    {
        var moved = new List<ProjectBriefingMovedProject>();
        foreach (var before in generated.Facts)
        {
            if (!current.TryGetValue(before.ProjectId, out var facts))
            {
                continue;
            }

            var now = facts.ToSlideFact(before.Shown);
            var stage = !string.Equals(before.Stage, now.Stage, StringComparison.Ordinal);
            var status = !string.Equals(before.Status, now.Status, StringComparison.Ordinal);
            var cost = !string.Equals(before.Costs, now.Costs, StringComparison.Ordinal);
            var keyPoints = !string.Equals(before.KeyPoints, now.KeyPoints, StringComparison.Ordinal);
            if (stage || status || cost || keyPoints)
            {
                moved.Add(new ProjectBriefingMovedProject(before.ProjectId, facts.ProjectName, stage, status, cost, keyPoints));
            }
        }

        return moved.Count == 0
            ? ProjectBriefingDataMovement.None
            : new ProjectBriefingDataMovement(
                moved.Count(project => project.Stage),
                moved.Count(project => project.Status),
                moved.Count(project => project.Cost),
                moved,
                moved.Count(project => project.KeyPoints));
    }

    private static string Amount(ProjectBriefingCostValue? value)
        => value?.AmountInRupees is decimal amount && amount >= 0m
            ? decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    // A stable short digest (string.GetHashCode differs between processes).
    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 8);
}
