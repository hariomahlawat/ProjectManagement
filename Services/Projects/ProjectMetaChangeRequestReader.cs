using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public static class ProjectMetaChangeRequestReader
{
    // SECTION: Metadata change request reader
    public static async Task<ProjectMetaChangeRequestVm?> BuildAsync(
        ApplicationDbContext db,
        ProjectMetaChangeRequest request,
        Project project,
        CancellationToken ct)
    {
        if (db is null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        ProjectMetaChangeRequestPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<ProjectMetaChangeRequestPayload>(request.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            var logger = db.GetService<ILogger<ProjectMetaChangeRequestReader>>();
            logger?.LogError(ex, "Failed to parse meta change payload for request {RequestId}.", request.Id);
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        static string Format(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        var originalNameDisplay = Format(request.OriginalName);
        var originalDescriptionDisplay = Format(request.OriginalDescription);
        var originalCaseFileDisplay = Format(request.OriginalCaseFileNumber);
        var proposedNameRaw = string.IsNullOrWhiteSpace(payload.Name) ? project.Name : payload.Name.Trim();
        var proposedNameDisplay = Format(proposedNameRaw);
        var proposedDescription = string.IsNullOrWhiteSpace(payload.Description) ? null : payload.Description.Trim();
        var proposedDescriptionDisplay = Format(proposedDescription);
        var proposedCaseFileNumber = string.IsNullOrWhiteSpace(payload.CaseFileNumber) ? null : payload.CaseFileNumber.Trim();
        var proposedCaseFileDisplay = Format(proposedCaseFileNumber);
        var proposedCategoryId = payload.CategoryId;
        var proposedTechnicalCategoryId = payload.TechnicalCategoryId;
        var proposedProjectTypeId = payload.ProjectTypeId ?? project.ProjectTypeId;
        var proposedIsBuild = payload.IsBuild ?? project.IsBuild;
        var proposedUnitId = payload.SponsoringUnitId;
        var proposedLineDirectorateId = payload.SponsoringLineDirectorateId;

        var categoryPath = project.CategoryId.HasValue
            ? await BuildCategoryPathAsync(db, project.CategoryId.Value, ct)
            : Array.Empty<ProjectCategory>();
        var technicalPath = project.TechnicalCategoryId.HasValue
            ? await BuildTechnicalCategoryPathAsync(db, project.TechnicalCategoryId.Value, ct)
            : Array.Empty<TechnicalCategory>();

        var currentUnitDisplay = Format(project.SponsoringUnit?.Name);
        var currentLineDirectorateDisplay = Format(project.SponsoringLineDirectorate?.Name);

        var originalTechnicalCategoryDisplay = "—";
        if (request.OriginalTechnicalCategoryId.HasValue)
        {
            var originalTechnicalPath = await BuildTechnicalCategoryPathAsync(db, request.OriginalTechnicalCategoryId.Value, ct);
            if (originalTechnicalPath.Any())
            {
                originalTechnicalCategoryDisplay = string.Join(" › ", originalTechnicalPath.Select(c => c.Name));
            }
        }

        var currentTechnicalCategoryDisplay = technicalPath.Any()
            ? string.Join(" › ", technicalPath.Select(c => c.Name))
            : "—";

        var originalUnitName = request.OriginalSponsoringUnitId.HasValue
            ? await db.SponsoringUnits.AsNoTracking()
                .Where(u => u.Id == request.OriginalSponsoringUnitId.Value)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(ct)
            : null;
        var originalUnitDisplay = request.OriginalSponsoringUnitId.HasValue
            ? (string.IsNullOrWhiteSpace(originalUnitName) ? "(inactive)" : Format(originalUnitName))
            : "—";

        var originalLineName = request.OriginalSponsoringLineDirectorateId.HasValue
            ? await db.LineDirectorates.AsNoTracking()
                .Where(l => l.Id == request.OriginalSponsoringLineDirectorateId.Value)
                .Select(l => l.Name)
                .FirstOrDefaultAsync(ct)
            : null;
        var originalLineDisplay = request.OriginalSponsoringLineDirectorateId.HasValue
            ? (string.IsNullOrWhiteSpace(originalLineName) ? "(inactive)" : Format(originalLineName))
            : "—";

        string FormatBuildFlag(bool isBuild) => isBuild ? "Yes" : "No";

        async Task<string> GetProjectTypeDisplayAsync(int? projectTypeId)
        {
            if (!projectTypeId.HasValue)
            {
                return "—";
            }

            var type = await db.ProjectTypes.AsNoTracking()
                .Where(t => t.Id == projectTypeId.Value)
                .Select(t => new { t.Name, t.IsActive })
                .FirstOrDefaultAsync(ct);

            if (type is null)
            {
                return "(inactive)";
            }

            return type.IsActive ? type.Name : $"{type.Name} (inactive)";
        }

        string proposedUnitDisplay;
        if (proposedUnitId.HasValue)
        {
            var proposedUnitName = await db.SponsoringUnits.AsNoTracking()
                .Where(u => u.Id == proposedUnitId.Value)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(ct);
            proposedUnitDisplay = string.IsNullOrWhiteSpace(proposedUnitName) ? "(inactive)" : Format(proposedUnitName);
        }
        else
        {
            proposedUnitDisplay = "—";
        }

        string proposedLineDisplay;
        if (proposedLineDirectorateId.HasValue)
        {
            var proposedLineName = await db.LineDirectorates.AsNoTracking()
                .Where(l => l.Id == proposedLineDirectorateId.Value)
                .Select(l => l.Name)
                .FirstOrDefaultAsync(ct);
            proposedLineDisplay = string.IsNullOrWhiteSpace(proposedLineName) ? "(inactive)" : Format(proposedLineName);
        }
        else
        {
            proposedLineDisplay = "—";
        }

        var originalProjectTypeDisplay = await GetProjectTypeDisplayAsync(request.OriginalProjectTypeId);
        var currentProjectTypeDisplay = await GetProjectTypeDisplayAsync(project.ProjectTypeId);
        var proposedProjectTypeDisplay = await GetProjectTypeDisplayAsync(proposedProjectTypeId);

        var originalBuildDisplay = FormatBuildFlag(request.OriginalIsBuild);
        var currentBuildDisplay = FormatBuildFlag(project.IsBuild);
        var proposedBuildDisplay = FormatBuildFlag(proposedIsBuild);

        var originalCategoryDisplay = "—";
        if (request.OriginalCategoryId.HasValue)
        {
            var originalPath = await BuildCategoryPathAsync(db, request.OriginalCategoryId.Value, ct);
            if (originalPath.Any())
            {
                originalCategoryDisplay = string.Join(" › ", originalPath.Select(c => c.Name));
            }
        }

        var currentCategoryDisplay = categoryPath.Any()
            ? string.Join(" › ", categoryPath.Select(c => c.Name))
            : "—";

        string proposedCategoryDisplay = "—";
        if (proposedCategoryId.HasValue)
        {
            var proposedPath = await BuildCategoryPathAsync(db, proposedCategoryId.Value, ct);
            if (proposedPath.Any())
            {
                proposedCategoryDisplay = string.Join(" › ", proposedPath.Select(c => c.Name));
            }
        }

        string proposedTechnicalCategoryDisplay = "—";
        if (proposedTechnicalCategoryId.HasValue)
        {
            var proposedTechnicalPath = await BuildTechnicalCategoryPathAsync(db, proposedTechnicalCategoryId.Value, ct);
            if (proposedTechnicalPath.Any())
            {
                proposedTechnicalCategoryDisplay = string.Join(" › ", proposedTechnicalPath.Select(c => c.Name));
            }
        }

        var requestedBy = await GetDisplayNameAsync(db, request.RequestedByUserId, ct);

        var driftFields = ProjectMetaChangeDriftDetector.Detect(project, request);
        var drift = new List<ProjectMetaChangeDriftVm>();

        foreach (var field in driftFields)
        {
            switch (field)
            {
                case ProjectMetaChangeDriftFields.Name:
                    drift.Add(new ProjectMetaChangeDriftVm("Name", originalNameDisplay, Format(project.Name), false));
                    break;
                case ProjectMetaChangeDriftFields.Description:
                    drift.Add(new ProjectMetaChangeDriftVm("Description", originalDescriptionDisplay, Format(project.Description), false));
                    break;
                case ProjectMetaChangeDriftFields.CaseFileNumber:
                    drift.Add(new ProjectMetaChangeDriftVm("Case file number", originalCaseFileDisplay, Format(project.CaseFileNumber), false));
                    break;
                case ProjectMetaChangeDriftFields.Category:
                    drift.Add(new ProjectMetaChangeDriftVm("Category", originalCategoryDisplay, currentCategoryDisplay, false));
                    break;
                case ProjectMetaChangeDriftFields.TechnicalCategory:
                    drift.Add(new ProjectMetaChangeDriftVm("Technical category", originalTechnicalCategoryDisplay, currentTechnicalCategoryDisplay, false));
                    break;
                case ProjectMetaChangeDriftFields.ProjectType:
                    drift.Add(new ProjectMetaChangeDriftVm("Project type", originalProjectTypeDisplay, currentProjectTypeDisplay, false));
                    break;
                case ProjectMetaChangeDriftFields.IsBuild:
                    drift.Add(new ProjectMetaChangeDriftVm("Build (repeat / re-manufacture)", originalBuildDisplay, currentBuildDisplay, false));
                    break;
                case ProjectMetaChangeDriftFields.SponsoringUnit:
                    drift.Add(new ProjectMetaChangeDriftVm("Sponsoring Unit", originalUnitDisplay, currentUnitDisplay, false));
                    break;
                case ProjectMetaChangeDriftFields.SponsoringLineDirectorate:
                    drift.Add(new ProjectMetaChangeDriftVm("Sponsoring Line Dte", originalLineDisplay, currentLineDirectorateDisplay, false));
                    break;
                case ProjectMetaChangeDriftFields.ProjectRecord:
                    drift.Add(new ProjectMetaChangeDriftVm("Project record", "Submission snapshot", "Updated after submission", true));
                    break;
            }
        }

        var nameField = new ProjectMetaChangeFieldVm(
            project.Name,
            proposedNameDisplay,
            !string.Equals(project.Name, proposedNameRaw, StringComparison.Ordinal));
        var descriptionField = new ProjectMetaChangeFieldVm(
            Format(project.Description),
            proposedDescriptionDisplay,
            !string.Equals(project.Description ?? string.Empty, proposedDescription ?? string.Empty, StringComparison.Ordinal));
        var caseFileField = new ProjectMetaChangeFieldVm(
            Format(project.CaseFileNumber),
            proposedCaseFileDisplay,
            !string.Equals(project.CaseFileNumber ?? string.Empty, proposedCaseFileNumber ?? string.Empty, StringComparison.Ordinal));
        var categoryField = new ProjectMetaChangeFieldVm(
            currentCategoryDisplay,
            proposedCategoryDisplay,
            project.CategoryId != proposedCategoryId);
        var technicalCategoryField = new ProjectMetaChangeFieldVm(
            currentTechnicalCategoryDisplay,
            proposedTechnicalCategoryDisplay,
            project.TechnicalCategoryId != proposedTechnicalCategoryId);
        var projectTypeField = new ProjectMetaChangeFieldVm(
            currentProjectTypeDisplay,
            proposedProjectTypeDisplay,
            project.ProjectTypeId != proposedProjectTypeId);
        var buildField = new ProjectMetaChangeFieldVm(
            currentBuildDisplay,
            proposedBuildDisplay,
            project.IsBuild != proposedIsBuild);
        var unitField = new ProjectMetaChangeFieldVm(
            currentUnitDisplay,
            proposedUnitDisplay,
            project.SponsoringUnitId != proposedUnitId);
        var lineDirectorateField = new ProjectMetaChangeFieldVm(
            currentLineDirectorateDisplay,
            proposedLineDisplay,
            project.SponsoringLineDirectorateId != proposedLineDirectorateId);

        var summaryFields = new List<string>();

        void AddSummary(ProjectMetaChangeFieldVm field, string label)
        {
            if (field.HasChanged)
            {
                summaryFields.Add(label);
            }
        }

        AddSummary(nameField, "name");
        AddSummary(descriptionField, "description");
        AddSummary(caseFileField, "case file number");
        AddSummary(categoryField, "category");
        AddSummary(technicalCategoryField, "technical category");
        AddSummary(projectTypeField, "project type");
        AddSummary(buildField, "build flag");
        AddSummary(unitField, "sponsoring unit");
        AddSummary(lineDirectorateField, "sponsoring line directorate");

        string summary;
        if (summaryFields.Count == 0)
        {
            summary = "Requested metadata review.";
        }
        else if (summaryFields.Count == 1)
        {
            summary = string.Format(CultureInfo.InvariantCulture, "Requested update to {0}.", summaryFields[0]);
        }
        else if (summaryFields.Count == 2)
        {
            summary = string.Format(CultureInfo.InvariantCulture, "Requested updates to {0} and {1}.", summaryFields[0], summaryFields[1]);
        }
        else
        {
            var leading = string.Join(", ", summaryFields.Take(summaryFields.Count - 1));
            summary = string.Format(CultureInfo.InvariantCulture, "Requested updates to {0}, and {1}.", leading, summaryFields[^1]);
        }

        return new ProjectMetaChangeRequestVm
        {
            RequestId = request.Id,
            RequestedBy = requestedBy,
            RequestedByUserId = request.RequestedByUserId,
            RequestedOnUtc = request.RequestedOnUtc,
            RequestNote = request.RequestNote,
            OriginalName = originalNameDisplay,
            OriginalDescription = originalDescriptionDisplay,
            OriginalCaseFileNumber = originalCaseFileDisplay,
            OriginalCategory = originalCategoryDisplay,
            OriginalTechnicalCategory = originalTechnicalCategoryDisplay,
            OriginalProjectType = originalProjectTypeDisplay,
            OriginalIsBuild = originalBuildDisplay,
            Name = nameField,
            Description = descriptionField,
            CaseFileNumber = caseFileField,
            Category = categoryField,
            TechnicalCategory = technicalCategoryField,
            ProjectType = projectTypeField,
            IsBuild = buildField,
            SponsoringUnit = unitField,
            SponsoringLineDirectorate = lineDirectorateField,
            HasDrift = drift.Count > 0,
            Drift = drift,
            Summary = summary
        };
    }

    // SECTION: Supporting helpers
    private static async Task<string> GetDisplayNameAsync(ApplicationDbContext db, string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "Unknown";
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.UserName, u.Email })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return "Unknown";
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName;
        }

        return user.Email ?? userId;
    }

    private static async Task<IReadOnlyList<ProjectCategory>> BuildCategoryPathAsync(
        ApplicationDbContext db,
        int categoryId,
        CancellationToken ct)
    {
        var path = new List<ProjectCategory>();
        var visited = new HashSet<int>();
        var currentId = categoryId;

        while (true)
        {
            if (!visited.Add(currentId))
            {
                break;
            }

            var category = await db.ProjectCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentId, ct);
            if (category is null)
            {
                break;
            }

            path.Insert(0, category);

            if (category.ParentId is null)
            {
                break;
            }

            currentId = category.ParentId.Value;
        }

        return path;
    }

    private static async Task<IReadOnlyList<TechnicalCategory>> BuildTechnicalCategoryPathAsync(
        ApplicationDbContext db,
        int technicalCategoryId,
        CancellationToken ct)
    {
        var path = new List<TechnicalCategory>();
        var visited = new HashSet<int>();
        var currentId = technicalCategoryId;

        while (true)
        {
            if (!visited.Add(currentId))
            {
                break;
            }

            var category = await db.TechnicalCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentId, ct);
            if (category is null)
            {
                break;
            }

            path.Insert(0, category);

            if (category.ParentId is null)
            {
                break;
            }

            currentId = category.ParentId.Value;
        }

        return path;
    }
}
