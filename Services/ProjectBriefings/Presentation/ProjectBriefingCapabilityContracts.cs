namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public enum ProjectBriefingCapabilityBlockType
{
    Paragraph = 1,
    Heading = 2,
    Bullet = 3,
    NumberedItem = 4,
    LetteredItem = 5
}

public sealed record ProjectBriefingCapabilityBlock(
    ProjectBriefingCapabilityBlockType Type,
    string Text,
    string? Marker = null,
    bool IsContinuation = false,
    bool IsMuted = false);

public sealed record ProjectBriefingCapabilityLayoutBlock(
    ProjectBriefingCapabilityBlockType Type,
    string Text,
    string? Marker,
    bool IsContinuation,
    bool IsMuted,
    double FontSize,
    double TextHeight,
    double SpaceAfter)
{
    public double TotalHeight => TextHeight + SpaceAfter;
}

public sealed record ProjectBriefingCapabilityPage(
    int PageNumber,
    bool IsPrimary,
    IReadOnlyList<ProjectBriefingCapabilityLayoutBlock> Blocks);

public sealed class ProjectBriefingCapabilityPagination
{
    public ProjectBriefingCapabilityPagination(IReadOnlyList<ProjectBriefingCapabilityPage> pages)
    {
        Pages = pages ?? throw new ArgumentNullException(nameof(pages));
    }

    public IReadOnlyList<ProjectBriefingCapabilityPage> Pages { get; }

    public int ContinuationSlideCount => Math.Max(0, Pages.Count - 1);
}
