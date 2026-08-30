using System;
using System.Linq.Expressions;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Defines the single business rule for whether Transfer of Technology is
/// operationally applicable to a project. Repeat-build/remanufacture projects
/// are outside the ToT universe and must not own any ToT state, requests or
/// ToT-scoped child data.
/// </summary>
public static class ProjectTotApplicabilityPolicy
{
    public static readonly Expression<Func<Project, bool>> EligibleProjectPredicate = project =>
        !project.IsDeleted
        && !project.IsArchived
        && !project.IsBuild
        && project.LifecycleStatus == ProjectLifecycleStatus.Completed;

    public static readonly Expression<Func<ProjectTot, bool>> EligibleTotPredicate = tot =>
        !tot.Project.IsDeleted
        && !tot.Project.IsArchived
        && !tot.Project.IsBuild
        && tot.Project.LifecycleStatus == ProjectLifecycleStatus.Completed;

    public static bool IsApplicable(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return GetIneligibilityReason(project) is null;
    }

    public static string? GetIneligibilityReason(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.IsDeleted)
        {
            return "Transfer of Technology cannot be changed for a deleted project.";
        }

        if (project.IsArchived)
        {
            return "Transfer of Technology cannot be changed while the project is archived.";
        }

        if (project.IsBuild)
        {
            return "Transfer of Technology is not applicable to Repeat Build projects.";
        }

        return project.LifecycleStatus == ProjectLifecycleStatus.Completed
            ? null
            : "Transfer of Technology can be changed only after the project is completed.";
    }
}
