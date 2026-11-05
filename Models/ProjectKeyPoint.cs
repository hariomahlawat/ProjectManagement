using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

/// <summary>
/// The fixed questions a project's key points answer. The set and its order are the same for every
/// project and deck; the headings and hints live in <c>ProjectKeyPointCatalog</c>. A value's meaning
/// never changes once points are written against it.
/// </summary>
public enum ProjectKeyPointKind : short
{
    TrainingGap = 1,
    OpNecessity = 2,
    TrainingRealism = 3,
    EaseOfLearning = 4,
    ProliferationPotential = 5
}

/// <summary>
/// One short, optional point on why a project matters (one or two lines on a briefing slide).
/// A project has at most one point per kind; a kind with no point has no row.
/// </summary>
public sealed class ProjectKeyPoint
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public ProjectKeyPointKind Kind { get; set; }

    [Required]
    [MaxLength(ProjectFieldLimits.KeyPointMaxLength)]
    public string Text { get; set; } = string.Empty;
}
