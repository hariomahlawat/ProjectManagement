using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ConferenceRemarks;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// IT projects on the conference review (owner, 9 Nov 2026). IT projects have no assigned
/// Project Officer: the ITO runs them, so every live IT project appears under each officer who
/// holds the ITO role, and an ITO with no other workload is still listed. Directions are the IT
/// project's own conference remarks (<see cref="ItProjectRemark"/>, Type = Conference).
/// </summary>
public sealed partial class OfficerConferenceReadService
{
    private sealed record ItProjectConferenceRow(
        int Id,
        string Title,
        string Reference,
        int FinancialYearStart,
        ItProjectStatus Status,
        ItProjectTiming Timing,
        ItProjectYearTypeInfo? YearType);

    /// <summary>Live IT projects (not started or in progress), in board order.</summary>
    private async Task<IReadOnlyList<ItProjectConferenceRow>> LoadLiveItProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.ItProjects
            .AsNoTracking()
            .Where(project => !project.IsDeleted && project.DroppedOn == null)
            .Include(project => project.Stages)
            .Include(project => project.FinancialYears)
            .ToListAsync(cancellationToken);

        var today = ItProjectYearTypes.TodayIst(_clock.UtcNow);
        var currentYear = ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow);
        return projects
            .Select(project => (Project: project, Status: ItProjectStatus.Resolve(project)))
            .Where(item => item.Status.Kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress)
            .Select(item => new ItProjectConferenceRow(
                item.Project.Id,
                item.Project.Title,
                item.Project.PppReferenceNumber,
                item.Project.FinancialYearStart,
                item.Status,
                ItProjectTiming.Resolve(item.Project, today, ToIstDate),
                ItProjectYearTypes.Resolve(item.Project, currentYear, currentYear)))
            .OrderBy(row => row.Status.CurrentStageCode is null ? int.MaxValue : ItProjectStages.OrderOf(row.Status.CurrentStageCode))
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gives every officer who holds the ITO role the live IT projects, and adds ITO officers who
    /// have no other conference workload. Nothing changes when there is no ITO or no live IT project.
    /// </summary>
    private async Task<IReadOnlyList<ConferenceOfficerContext>> AttachItProjectsAsync(
        IReadOnlyList<ConferenceOfficerContext> officers,
        CancellationToken cancellationToken)
    {
        var itoUserIds = await LoadUserIdsInRoleAsync(RoleNames.Ito, cancellationToken);
        if (itoUserIds.Count == 0)
        {
            return officers;
        }

        var rows = await LoadLiveItProjectsAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return officers;
        }

        var result = officers
            .Select(officer => itoUserIds.Contains(officer.UserId) ? officer with { ItProjectsInput = rows } : officer)
            .ToList();

        var listed = officers.Select(officer => officer.UserId).ToHashSet(StringComparer.Ordinal);
        var missing = itoUserIds.Where(id => !listed.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            var identities = await _db.Users
                .AsNoTracking()
                .Where(user => missing.Contains(user.Id) && !user.IsDisabled && !user.PendingDeletion)
                .Select(user => new
                {
                    user.Id,
                    Name = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "ITO" : user.FullName,
                    user.Rank
                })
                .ToListAsync(cancellationToken);

            result.AddRange(identities
                .OrderBy(identity => OfficerRankOrder(identity.Rank))
                .ThenBy(identity => identity.Name, StringComparer.OrdinalIgnoreCase)
                .Select(identity => new ConferenceOfficerContext(
                    identity.Id,
                    identity.Name,
                    identity.Rank ?? string.Empty,
                    Array.Empty<CommandOfficerProjectVm>(),
                    Array.Empty<CommandOfficerIdeaVm>(),
                    Array.Empty<CommandOfficerTaskVm>(),
                    Array.Empty<ConferenceProjectCarryover>(),
                    rows)));
        }

        return result;
    }

    /// <summary>The "IT projects" section of an ITO officer's conference review.</summary>
    private async Task<OfficerConferenceSectionVm> BuildItProjectSectionAsync(
        ConferenceOfficerContext officer,
        CancellationToken cancellationToken)
    {
        var ids = officer.ItProjects.Select(row => row.Id).ToArray();
        var remarks = await _db.ItProjectRemarks
            .AsNoTracking()
            .Where(remark => ids.Contains(remark.ItProjectId) && !remark.IsDeleted)
            .ToListAsync(cancellationToken);
        var byProject = remarks
            .GroupBy(remark => remark.ItProjectId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var authorNames = await LoadAuthorNamesAsync(remarks.Select(remark => remark.AuthorUserId), cancellationToken);

        var items = new List<OfficerConferenceItemVm>(officer.ItProjects.Count);
        foreach (var row in officer.ItProjects)
        {
            var projectRemarks = byProject.TryGetValue(row.Id, out var found) ? found : new List<ItProjectRemark>();
            var directions = projectRemarks
                .Where(remark => remark.Type == ItProjectRemarkType.Conference)
                .OrderBy(remark => remark.CreatedAtUtc)
                .ThenBy(remark => remark.Id)
                .ToList();
            var latest = directions.LastOrDefault();

            var progress = new List<ConferenceProgressEntryVm>();
            if (latest is not null)
            {
                var reply = projectRemarks
                    .Where(remark => remark.Type == ItProjectRemarkType.General
                        && string.Equals(remark.AuthorUserId, officer.UserId, StringComparison.Ordinal)
                        && IsAfter(remark.CreatedAtUtc, remark.Id, latest.CreatedAtUtc, latest.Id))
                    .OrderByDescending(remark => remark.CreatedAtUtc)
                    .ThenByDescending(remark => remark.Id)
                    .FirstOrDefault();
                if (reply is not null)
                {
                    progress.Add(ItProjectProgressEntry(reply, authorNames));
                }
            }

            var stageLabel = row.Status.CurrentStageLabel;
            var stateName = row.Status.Kind == ItProjectStatusKind.NotStarted ? "Not started" : $"At {stageLabel}";
            var late = row.Timing.Basis == ItProjectTimeBasis.StageDate && row.Timing.Band == ItProjectAgeBand.Red;
            var type = row.YearType is { IsOwnYear: false } carried ? $" · {carried.Code}" : string.Empty;
            items.Add(new OfficerConferenceItemVm
            {
                Kind = ConferenceItemKind.ItProject,
                ItemId = row.Id,
                Title = row.Title,
                OpenUrl = $"/ItProjects/{row.Id}",
                // Code and name are the same, so the row shows the stage once ("At COB").
                CurrentStateCode = stateName,
                CurrentStateName = stateName,
                CurrentContext = $"{row.YearType?.PppReferenceNumber ?? row.Reference} · FY {FinancialYearHelper.Format(row.FinancialYearStart)}{type}",
                AttentionText = late ? $"{row.Timing.DaysAtStage} days at {stageLabel}" : null,
                RequiresAttention = late,
                LatestDirection = latest is null ? null : ItProjectDirection(latest, authorNames),
                DirectionCount = directions.Count,
                ProgressEntries = progress,
                EmptyProgressText = latest is not null && progress.Count == 0
                    ? "Progress update awaited. The ITO has not written a remark since the direction was issued."
                    : null,
                ProgressSummary = string.Empty,
                LatestProgressText = null
            });
        }

        return new OfficerConferenceSectionVm
        {
            Kind = ConferenceItemKind.ItProject,
            Title = "IT projects",
            IconClass = "bi-pc-display",
            Items = items
        };
    }

    /// <summary>Every conference direction on an IT project, each with the ITO's latest remark before the next one.</summary>
    private async Task<ConferenceDirectionHistoryVm> BuildItProjectDirectionHistoryAsync(
        string officerUserId,
        int itProjectId,
        CancellationToken cancellationToken)
    {
        var remarks = await _db.ItProjectRemarks
            .AsNoTracking()
            .Where(remark => remark.ItProjectId == itProjectId && !remark.IsDeleted)
            .OrderBy(remark => remark.CreatedAtUtc)
            .ThenBy(remark => remark.Id)
            .ToListAsync(cancellationToken);
        var directions = remarks.Where(remark => remark.Type == ItProjectRemarkType.Conference).ToList();
        if (directions.Count == 0)
        {
            return EmptyHistory(ConferenceItemKind.ItProject, itProjectId);
        }

        var replies = remarks
            .Where(remark => remark.Type == ItProjectRemarkType.General
                && string.Equals(remark.AuthorUserId, officerUserId, StringComparison.Ordinal))
            .ToList();
        var authorNames = await LoadAuthorNamesAsync(remarks.Select(remark => remark.AuthorUserId), cancellationToken);

        var cycles = new List<ConferenceDirectionCycleVm>(directions.Count);
        for (var index = 0; index < directions.Count; index++)
        {
            var direction = directions[index];
            var next = index + 1 < directions.Count ? directions[index + 1] : null;
            var reply = replies
                .Where(remark => IsAfter(remark.CreatedAtUtc, remark.Id, direction.CreatedAtUtc, direction.Id)
                    && (next is null || IsBefore(remark.CreatedAtUtc, remark.Id, next.CreatedAtUtc, next.Id)))
                .OrderByDescending(remark => remark.CreatedAtUtc)
                .ThenByDescending(remark => remark.Id)
                .FirstOrDefault();
            var progress = reply is null
                ? Array.Empty<ConferenceProgressEntryVm>()
                : new[] { ItProjectProgressEntry(reply, authorNames) };

            cycles.Add(new ConferenceDirectionCycleVm
            {
                Direction = ItProjectDirection(direction, authorNames),
                ProgressEntries = progress,
                EmptyProgressText = progress.Length == 0
                    ? next is null
                        ? "Progress update awaited. The ITO has not written a remark since the direction was issued."
                        : "The ITO wrote no remark before the next conference direction."
                    : null,
                SequenceNumber = index + 1,
                TotalDirections = directions.Count,
                IsLatest = index == directions.Count - 1
            });
        }

        return new ConferenceDirectionHistoryVm
        {
            Kind = ConferenceItemKind.ItProject,
            ItemId = itProjectId,
            Cycles = cycles
        };
    }

    private static ConferenceDirectionVm ItProjectDirection(ItProjectRemark direction, IReadOnlyDictionary<string, string> authorNames)
        => new()
        {
            Id = direction.Id,
            Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
            AuthorName = ResolveAuthor(authorNames, direction.AuthorUserId),
            AuthorRole = DisplayRole(direction.AuthorRole),
            CreatedAtUtc = AsUtc(direction.CreatedAtUtc),
            SnapshotLabel = "Stage when issued",
            SnapshotValue = direction.StageCode is { } code && ItProjectStages.IsKnown(code) ? ItProjectStages.LabelOf(code) : "Not recorded"
        };

    private static ConferenceProgressEntryVm ItProjectProgressEntry(ItProjectRemark remark, IReadOnlyDictionary<string, string> authorNames)
        => new()
        {
            Label = "Latest remark",
            Body = ConferenceDirectionTextFormatter.ToDisplayText(remark.Body),
            AuthorName = ResolveAuthor(authorNames, remark.AuthorUserId),
            ActivityAtUtc = AsUtc(remark.CreatedAtUtc)
        };

    private static DateOnly ToIstDate(DateTime utc) => DateOnly.FromDateTime(IstClock.ToIst(AsUtc(utc)));
}
