using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public static class ProjectMetaChangeDriftFields
{
    public const string Name = nameof(Project.Name);
    public const string Description = nameof(Project.Description);
    public const string CaseFileNumber = nameof(Project.CaseFileNumber);
    public const string Category = nameof(Project.CategoryId);
    public const string TechnicalCategory = nameof(Project.TechnicalCategoryId);
    public const string SponsoringUnit = nameof(Project.SponsoringUnitId);
    public const string SponsoringLineDirectorate = nameof(Project.SponsoringLineDirectorateId);
    public const string ProjectRecord = "ProjectRecord";
}

public static class ProjectMetaChangeDriftDetector
{
    public static IReadOnlyCollection<string> Detect(Project project, ProjectMetaChangeRequest request)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var fields = new List<string>();

        if (!string.Equals(request.OriginalName, project.Name, StringComparison.Ordinal))
        {
            fields.Add(ProjectMetaChangeDriftFields.Name);
        }

        var requestDescription = request.OriginalDescription ?? string.Empty;
        var projectDescription = project.Description ?? string.Empty;
        if (!string.Equals(requestDescription, projectDescription, StringComparison.Ordinal))
        {
            fields.Add(ProjectMetaChangeDriftFields.Description);
        }

        var requestCaseFile = request.OriginalCaseFileNumber ?? string.Empty;
        var projectCaseFile = project.CaseFileNumber ?? string.Empty;
        if (!string.Equals(requestCaseFile, projectCaseFile, StringComparison.Ordinal))
        {
            fields.Add(ProjectMetaChangeDriftFields.CaseFileNumber);
        }

        if (request.OriginalCategoryId != project.CategoryId)
        {
            fields.Add(ProjectMetaChangeDriftFields.Category);
        }

        if (request.OriginalTechnicalCategoryId != project.TechnicalCategoryId)
        {
            fields.Add(ProjectMetaChangeDriftFields.TechnicalCategory);
        }

        if (request.OriginalSponsoringUnitId != project.SponsoringUnitId)
        {
            fields.Add(ProjectMetaChangeDriftFields.SponsoringUnit);
        }

        if (request.OriginalSponsoringLineDirectorateId != project.SponsoringLineDirectorateId)
        {
            fields.Add(ProjectMetaChangeDriftFields.SponsoringLineDirectorate);
        }

        if (request.OriginalRowVersion is { Length: > 0 }
            && project.RowVersion is { Length: > 0 }
            && !request.OriginalRowVersion.SequenceEqual(project.RowVersion))
        {
            fields.Add(ProjectMetaChangeDriftFields.ProjectRecord);
        }

        return fields;
    }
}
