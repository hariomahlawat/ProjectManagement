using System;
using System.Linq.Expressions;
using ProjectManagement.Models;

namespace ProjectManagement.Application.Ipr;

/// <summary>
/// Defines whether a PRISM project may own an IPR record.
/// Repeat Build / re-manufacture projects are implementation instances of an existing capability
/// and must not receive independent IPR attribution. Archived original projects remain valid
/// historical owners of IPR; soft-deleted projects do not.
/// </summary>
public static class IprProjectEligibilityPolicy
{
    public static readonly Expression<Func<Project, bool>> EligibleProjectPredicate =
        project => !project.IsDeleted && !project.IsBuild;

    public static bool IsEligible(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return !project.IsDeleted && !project.IsBuild;
    }
}
