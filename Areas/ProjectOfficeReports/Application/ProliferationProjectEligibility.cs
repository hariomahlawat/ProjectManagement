using System.Linq;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// Canonical project eligibility policy for Proliferation.
/// Historical records may reference repeat-build projects, but new proliferation
/// records and new counting-rule exceptions must use an original completed project.
/// </summary>
public static class ProliferationProjectEligibility
{
    public const string RepeatBuildRecordError =
        "Repeat-build/remanufacture projects cannot be used for new proliferation records. Link proliferation against the original completed project.";

    public const string RepeatBuildPreferenceError =
        "Repeat-build/remanufacture projects cannot be used for new proliferation counting rules. Configure the rule against the original completed project.";

    public static IQueryable<Project> CompletedVisibleProjects(IQueryable<Project> projects)
        => projects.Where(project =>
            !project.IsDeleted &&
            !project.IsArchived &&
            project.LifecycleStatus == ProjectLifecycleStatus.Completed);

    public static IQueryable<Project> EligibleForNewRecords(IQueryable<Project> projects)
        => CompletedVisibleProjects(projects).Where(project => !project.IsBuild);

    public static bool IsEligibleForNewRecord(Project project)
        => project is not null
           && !project.IsDeleted
           && !project.IsArchived
           && project.LifecycleStatus == ProjectLifecycleStatus.Completed
           && !project.IsBuild;
}
