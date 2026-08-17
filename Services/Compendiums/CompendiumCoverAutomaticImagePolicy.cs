namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Deterministic cover-image ranking shared by the Cover Editor and final PDF export.
/// The policy intentionally relies only on publication state that exists in both paths,
/// preventing browser/export drift from separate ranking algorithms.
/// </summary>
public static class CompendiumCoverAutomaticImagePolicy
{
    public sealed record ProjectSource(
        int ProjectId,
        int? CoverPhotoId,
        double FocalX,
        double FocalY,
        int SortOrder);

    public sealed record Candidate(
        int ProjectId,
        int PhotoId,
        double FocalX,
        double FocalY,
        int Priority);

    private const int SuitablePriority = 800_000;
    private const int PreferredPriority = 550_000;
    private const int ResolvedCoverPriority = 220_000;

    public static IReadOnlyList<Candidate> BuildCandidates(
        IEnumerable<ProjectSource> projects,
        IEnumerable<CompendiumPhotoPreference>? preferences)
    {
        var sourceByProject = projects
            .Where(project => project.ProjectId > 0)
            .GroupBy(project => project.ProjectId)
            .Select(group => group.OrderBy(project => project.SortOrder).First())
            .ToDictionary(project => project.ProjectId);

        var candidates = new List<Candidate>();
        foreach (var preference in preferences ?? Array.Empty<CompendiumPhotoPreference>())
        {
            if (!sourceByProject.TryGetValue(preference.ProjectId, out var source)
                || preference.PhotoId <= 0
                || (!preference.SuitableForCoverHero && !preference.PreferredForPublication))
            {
                continue;
            }

            var basePriority = preference.SuitableForCoverHero
                ? SuitablePriority
                : PreferredPriority;
            var focalX = source.CoverPhotoId == preference.PhotoId ? ClampFocal(source.FocalX) : .5d;
            var focalY = source.CoverPhotoId == preference.PhotoId ? ClampFocal(source.FocalY) : .5d;
            candidates.Add(new Candidate(
                source.ProjectId,
                preference.PhotoId,
                focalX,
                focalY,
                ApplyStableOrder(basePriority, source.SortOrder)));
        }

        foreach (var source in sourceByProject.Values)
        {
            if (source.CoverPhotoId is not int photoId || photoId <= 0)
            {
                continue;
            }

            candidates.Add(new Candidate(
                source.ProjectId,
                photoId,
                ClampFocal(source.FocalX),
                ClampFocal(source.FocalY),
                ApplyStableOrder(ResolvedCoverPriority, source.SortOrder)));
        }

        return candidates
            .GroupBy(candidate => (candidate.ProjectId, candidate.PhotoId))
            .Select(group => group
                .OrderByDescending(candidate => candidate.Priority)
                .First())
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => sourceByProject[candidate.ProjectId].SortOrder)
            .ThenBy(candidate => candidate.ProjectId)
            .ThenBy(candidate => candidate.PhotoId)
            .ToArray();
    }

    public static IReadOnlyList<Candidate> BuildCandidates(
        IReadOnlyList<CompendiumProjectDto> projects,
        IReadOnlyList<CompendiumPhotoPreference>? preferences)
        => BuildCandidates(
            projects.Select((project, index) => new ProjectSource(
                project.ProjectId,
                project.CoverPhotoId,
                project.PrimaryFocalX,
                project.PrimaryFocalY,
                project.SortOrder == 0 ? index : project.SortOrder)),
            preferences);

    private static int ApplyStableOrder(int priority, int sortOrder)
        => priority - Math.Clamp(sortOrder, 0, 99_999);

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;
}
