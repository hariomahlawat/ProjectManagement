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
/// The projects of the IT projects engine on the conference review. IT projects have no assigned
/// Project Officer: the ITO runs them, so every live IT project appears under each officer who
/// holds the ITO role (owner, 9 Nov 2026), on the command review (where an ITO with no other
/// workload is still listed) and in that officer's own conference view. MOLTI and SRE projects
/// appear under the Project Officer assigned to them, a section per module; an unassigned one
/// appears under nobody (docs/ux/molti-sre-projects.md). Directions are the project's own
/// conference remarks (<see cref="ItProjectRemark"/>, Type = Conference).
/// </summary>
public sealed partial class OfficerConferenceReadService
{
    /// <summary>Live IT projects (not started or in progress), in board order; shared with the Officer workload view.</summary>
    private Task<IReadOnlyList<LiveItProjectRow>> LoadLiveItProjectsAsync(CancellationToken cancellationToken)
        => LiveItProjects.LoadAsync(_db, _clock.UtcNow, cancellationToken);

    /// <summary>
    /// The live engine projects under each officer: the IT projects under every officer who holds
    /// the ITO role, then the MOLTI and SRE projects under their assigned officer. Officers with
    /// none are absent. <paramref name="itoUserIds"/> comes back so the caller can tell an ITO.
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, List<LiveItProjectRow>> ByOfficer, IReadOnlySet<string> ItoUserIds)> LoadEngineProjectsByOfficerAsync(
        CancellationToken cancellationToken)
    {
        var byOfficer = new Dictionary<string, List<LiveItProjectRow>>(StringComparer.Ordinal);
        var itoUserIds = await LoadUserIdsInRoleAsync(RoleNames.Ito, cancellationToken);
        if (itoUserIds.Count > 0)
        {
            var itRows = await LoadLiveItProjectsAsync(cancellationToken);
            if (itRows.Count > 0)
            {
                foreach (var id in itoUserIds)
                {
                    byOfficer[id] = itRows.ToList();
                }
            }
        }

        foreach (var row in await LiveItProjects.LoadAssignedAsync(_db, _clock.UtcNow, cancellationToken))
        {
            if (!byOfficer.TryGetValue(row.ProjectOfficerUserId!, out var rows))
            {
                byOfficer[row.ProjectOfficerUserId!] = rows = new List<LiveItProjectRow>();
            }

            rows.Add(row);
        }

        return (byOfficer, itoUserIds);
    }

    /// <summary>
    /// Gives each officer their live engine projects, and adds the officers who have them and no
    /// other conference workload. Nothing changes when there is nothing to add.
    /// </summary>
    private async Task<IReadOnlyList<ConferenceOfficerContext>> AttachItProjectsAsync(
        IReadOnlyList<ConferenceOfficerContext> officers,
        CancellationToken cancellationToken)
    {
        var (byOfficer, itoUserIds) = await LoadEngineProjectsByOfficerAsync(cancellationToken);
        if (byOfficer.Count == 0)
        {
            return officers;
        }

        var result = officers
            .Select(officer => byOfficer.TryGetValue(officer.UserId, out var rows) ? officer with { ItProjectsInput = rows } : officer)
            .ToList();

        var listed = officers.Select(officer => officer.UserId).ToHashSet(StringComparer.Ordinal);
        var missing = byOfficer.Keys.Where(id => !listed.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            var identities = await _db.Users
                .AsNoTracking()
                .Where(user => missing.Contains(user.Id) && !user.IsDisabled && !user.PendingDeletion)
                .Select(user => new
                {
                    user.Id,
                    Name = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "Officer" : user.FullName,
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
                    byOfficer[identity.Id],
                    // Listed only for these projects: an ITO, or an officer with nothing else under them.
                    ItProjectsOnly: IsEngineOnly(itoUserIds.Contains(identity.Id), byOfficer[identity.Id]))));
        }

        return result;
    }

    /// <summary>
    /// Whether an officer with no other workload shows only the engine sections. True for an ITO
    /// whose rows are all IT projects: the project, idea and task sections, with their "Add"
    /// forms, belong to Project Officers. An officer with MOLTI or SRE projects is a Project
    /// Officer and keeps the full review.
    /// </summary>
    private static bool IsEngineOnly(bool isIto, IReadOnlyCollection<LiveItProjectRow> rows)
        => isIto && rows.All(row => row.Module == ItProjectModules.It);

    /// <summary>The officer with their live engine projects (their own conference view).</summary>
    private async Task<ConferenceOfficerContext> WithOwnItProjectsAsync(
        ConferenceOfficerContext officer,
        CancellationToken cancellationToken)
    {
        var (byOfficer, itoUserIds) = await LoadEngineProjectsByOfficerAsync(cancellationToken);
        var isIto = itoUserIds.Contains(officer.UserId);
        IReadOnlyList<LiveItProjectRow> rows = byOfficer.TryGetValue(officer.UserId, out var found) ? found : Array.Empty<LiveItProjectRow>();
        if (!isIto && rows.Count == 0)
        {
            return officer;
        }

        // Same rule as the command review: an ITO with no Project Officer workload gets only the
        // IT projects section, not empty project, idea and task sections. With no live IT project
        // that section is shown empty ("No IT project in progress.").
        var noOtherWork = officer.ProjectCount == 0 && officer.Ideas.Count == 0 && officer.OtherTasks.Count == 0;
        if (rows.Count == 0 && !noOtherWork)
        {
            return officer;
        }

        return officer with { ItProjectsInput = rows, ItProjectsOnly = noOtherWork && IsEngineOnly(isIto, rows) };
    }

    /// <summary>
    /// One section per module the officer has live engine projects in, in catalogue order. An
    /// officer listed only as the ITO gets the IT projects section even when it is empty.
    /// </summary>
    private async Task<IReadOnlyList<OfficerConferenceSectionVm>> BuildItProjectSectionsAsync(
        ConferenceOfficerContext officer,
        CancellationToken cancellationToken)
    {
        var sections = new List<OfficerConferenceSectionVm>();
        foreach (var module in ItProjectModules.All)
        {
            var rows = officer.ItProjects.Where(row => row.Module == module.Code).ToList();
            if (rows.Count > 0 || (module.Code == ItProjectModules.It && officer.ItProjectsOnly))
            {
                sections.Add(await BuildItProjectSectionAsync(officer, module, rows, cancellationToken));
            }
        }

        return sections;
    }

    /// <summary>The engine projects under an officer, counted per module, for the header and the officer picker.</summary>
    private static IReadOnlyList<ConferenceModuleCountVm> ModuleCounts(IReadOnlyList<LiveItProjectRow> rows)
        => ItProjectModules.All
            .Select(module => new ConferenceModuleCountVm(rows.Count(row => row.Module == module.Code), module.Singular, module.Name))
            .Where(count => count.Count > 0)
            .ToList();

    /// <summary>One module's section of an officer's conference review: "IT projects", "MOLTI projects".</summary>
    private async Task<OfficerConferenceSectionVm> BuildItProjectSectionAsync(
        ConferenceOfficerContext officer,
        ItProjectModules.Definition module,
        IReadOnlyList<LiveItProjectRow> moduleRows,
        CancellationToken cancellationToken)
    {
        var runner = LiveItProjects.RunnerText(module.Code);
        var ids = moduleRows.Select(row => row.Id).ToArray();
        var remarks = await _db.ItProjectRemarks
            .AsNoTracking()
            .Where(remark => ids.Contains(remark.ItProjectId) && !remark.IsDeleted)
            .ToListAsync(cancellationToken);
        var byProject = remarks
            .GroupBy(remark => remark.ItProjectId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var authorNames = await LoadAuthorNamesAsync(remarks.Select(remark => remark.AuthorUserId), cancellationToken);

        var items = new List<OfficerConferenceItemVm>(moduleRows.Count);
        foreach (var row in moduleRows)
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
            var stateName = LiveItProjects.StateText(row.Status);
            var late = row.Timing.Basis == ItProjectTimeBasis.StageDate && row.Timing.Band == ItProjectAgeBand.Red;
            var type = row.YearType is { IsOwnYear: false } carried ? $" · {carried.Code}" : string.Empty;
            items.Add(new OfficerConferenceItemVm
            {
                Kind = ConferenceItemKind.ItProject,
                ItemId = row.Id,
                ItemLabel = module.Singular,
                Title = row.Title,
                OpenUrl = LiveItProjects.OpenUrl(row),
                // Code and name are the same, so the row shows the stage once ("At COB").
                CurrentStateCode = stateName,
                CurrentStateName = stateName,
                // The number, the FY and the CL / CF all belong to the same (current) year's IT ARPPP.
                CurrentContext = $"{row.YearType?.PppReferenceNumber ?? row.Reference} · FY {FinancialYearHelper.Format(row.YearType?.FinancialYearStart ?? row.FinancialYearStart)}{type}",
                AttentionText = late ? $"{row.Timing.DaysAtStage} days at {stageLabel}" : null,
                RequiresAttention = late,
                LatestDirection = latest is null ? null : ItProjectDirection(latest, authorNames, module.Code),
                DirectionCount = directions.Count,
                ProgressEntries = progress,
                EmptyProgressText = latest is not null && progress.Count == 0
                    ? $"Progress update awaited. {runner} has not written a remark since the direction was issued."
                    : null,
                ProgressSummary = string.Empty,
                LatestProgressText = null
            });
        }

        return new OfficerConferenceSectionVm
        {
            Kind = ConferenceItemKind.ItProject,
            Title = module.Name,
            IconClass = LiveItProjects.IconOf(module.Code),
            ModuleCode = module.Code,
            ItemLabel = module.Singular,
            Items = items
        };
    }

    /// <summary>Every conference direction on an engine project, each with its officer's latest remark before the next one.</summary>
    private async Task<ConferenceDirectionHistoryVm> BuildItProjectDirectionHistoryAsync(
        string officerUserId,
        int itProjectId,
        string module,
        CancellationToken cancellationToken)
    {
        var runner = LiveItProjects.RunnerText(module);
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
                Direction = ItProjectDirection(direction, authorNames, module),
                ProgressEntries = progress,
                EmptyProgressText = progress.Length == 0
                    ? next is null
                        ? $"Progress update awaited. {runner} has not written a remark since the direction was issued."
                        : $"{runner} wrote no remark before the next conference direction."
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

    private static ConferenceDirectionVm ItProjectDirection(ItProjectRemark direction, IReadOnlyDictionary<string, string> authorNames, string module)
        => new()
        {
            Id = direction.Id,
            Body = ConferenceDirectionTextFormatter.ToDisplayText(direction.Body),
            AuthorName = ResolveAuthor(authorNames, direction.AuthorUserId),
            AuthorRole = DisplayRole(direction.AuthorRole),
            CreatedAtUtc = AsUtc(direction.CreatedAtUtc),
            SnapshotLabel = "Stage when issued",
            SnapshotValue = direction.StageCode is { } code && ItProjectStages.IsKnown(code) ? ItProjectStages.LabelOf(code, module) : "Not recorded"
        };

    private static ConferenceProgressEntryVm ItProjectProgressEntry(ItProjectRemark remark, IReadOnlyDictionary<string, string> authorNames)
        => new()
        {
            Label = "Latest remark",
            Body = ConferenceDirectionTextFormatter.ToDisplayText(remark.Body),
            AuthorName = ResolveAuthor(authorNames, remark.AuthorUserId),
            ActivityAtUtc = AsUtc(remark.CreatedAtUtc)
        };

}
