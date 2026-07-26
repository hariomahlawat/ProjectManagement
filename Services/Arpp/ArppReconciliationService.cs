using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Arpp;

public sealed partial class ArppReconciliationService : IArppReconciliationService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ArppReconciliationService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ArppReconciliationResult> GetQueueAsync(
        int? financialYearStart,
        string? query,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim();
        var limit = Math.Clamp(take, 1, 250);

        var unlinked = _db.ArppEntries
            .AsNoTracking()
            .Where(entry => !entry.ProjectId.HasValue);

        if (financialYearStart.HasValue)
        {
            unlinked = unlinked.Where(entry => entry.Issue.FinancialYearStart == financialYearStart.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var pattern = $"%{normalizedQuery}%";
            unlinked = unlinked.Where(entry =>
                EF.Functions.ILike(entry.ProjectReference, pattern) ||
                EF.Functions.ILike(entry.SerialNumber, pattern) ||
                EF.Functions.ILike(entry.Issue.Name, pattern));
        }

        var total = await unlinked.CountAsync(cancellationToken);
        var rows = await unlinked
            .OrderByDescending(entry => entry.Issue.FinancialYearStart)
            .ThenByDescending(entry => entry.Issue.IssueSequence)
            .ThenBy(entry => entry.SortOrder)
            .Take(limit)
            .Select(entry => new
            {
                EntryId = entry.Id,
                EntryRowVersion = entry.RowVersion,
                IssueId = entry.ArppIssueId,
                entry.Issue.FinancialYearStart,
                IssueName = entry.Issue.Name,
                entry.Issue.IssueSequence,
                entry.Issue.IssueDate,
                entry.Issue.IsVerified,
                entry.SerialNumber,
                entry.ProjectReference,
                entry.IpaCost,
                entry.Category
            })
            .ToListAsync(cancellationToken);

        var availableYears = await _db.ArppEntries
            .AsNoTracking()
            .Where(entry => !entry.ProjectId.HasValue)
            .Select(entry => entry.Issue.FinancialYearStart)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new ArppReconciliationResult([], availableYears, total);
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Select(project => new CandidateProject(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.IsArchived
                    ? "Archived"
                    : project.LifecycleStatus == ProjectLifecycleStatus.Completed
                        ? "Completed"
                        : project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                            ? "Cancelled"
                            : "Ongoing"))
            .ToListAsync(cancellationToken);

        var projectIds = projects.Select(project => project.Id).ToArray();
        var legacyFacts = await _db.ProjectIpaFacts
            .AsNoTracking()
            .Where(fact => projectIds.Contains(fact.ProjectId))
            .Select(fact => new { fact.ProjectId, fact.IpaCost, fact.CreatedOnUtc, fact.Id })
            .ToListAsync(cancellationToken);

        var latestLegacy = legacyFacts
            .GroupBy(fact => fact.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(fact => fact.CreatedOnUtc)
                    .ThenByDescending(fact => fact.Id)
                    .Select(fact => (decimal?)fact.IpaCost)
                    .FirstOrDefault());

        var items = rows.Select(row =>
        {
            var suggestions = projects
                .Select(project => new
                {
                    Project = project,
                    Score = MatchScore(row.ProjectReference, project.Name, project.CaseFileNumber)
                })
                .Where(candidate => candidate.Score >= 0.30m)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Project.Name)
                .Take(3)
                .Select(candidate => new ArppProjectSuggestion(
                    candidate.Project.Id,
                    candidate.Project.Name,
                    candidate.Project.CaseFileNumber,
                    candidate.Project.StatusLabel,
                    latestLegacy.GetValueOrDefault(candidate.Project.Id),
                    (int)Math.Round(candidate.Score * 100m, MidpointRounding.AwayFromZero)))
                .ToArray();

            return new ArppReconciliationItem(
                row.EntryId,
                Convert.ToBase64String(row.EntryRowVersion),
                row.IssueId,
                row.FinancialYearStart,
                row.IssueName,
                row.IssueSequence,
                row.IssueDate,
                row.SerialNumber,
                row.ProjectReference,
                row.IpaCost,
                ProjectManagement.Models.Arpp.ArppDisplayNames.For(row.Category),
                row.IsVerified,
                suggestions);
        }).ToArray();

        return new ArppReconciliationResult(items, availableYears, total);
    }

    public async Task<ArppCommandResult> LinkAsync(
        ArppReconciliationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return ArppCommandResult.Failed("The current user could not be identified.");
        }

        var links = command.Links
            .Where(link => link.EntryId > 0 && link.ProjectId > 0)
            .GroupBy(link => link.EntryId)
            .Select(group => group.Last())
            .ToArray();

        if (links.Length == 0)
        {
            return ArppCommandResult.Failed("Select at least one PRISM project to link.");
        }

        var entryIds = links.Select(link => link.EntryId).ToArray();
        var projectIds = links.Select(link => link.ProjectId).Distinct().ToArray();

        var validProjectIds = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id) && !project.IsDeleted)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);

        if (validProjectIds.Count != projectIds.Length)
        {
            return ArppCommandResult.Failed("One or more selected PRISM projects are no longer available.");
        }

        var entries = await _db.ArppEntries
            .Include(entry => entry.Issue)
            .Where(entry => entryIds.Contains(entry.Id))
            .ToListAsync(cancellationToken);

        if (entries.Count != links.Length)
        {
            return ArppCommandResult.Failed("One or more ARPP rows no longer exist. Reload the reconciliation queue.");
        }

        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var linkByEntry = links.ToDictionary(link => link.EntryId);

        foreach (var entry in entries)
        {
            var link = linkByEntry[entry.Id];
            if (entry.ProjectId.HasValue)
            {
                AddError(errors, $"Entries[{entry.Id}]", "This ARPP row has already been linked. Reload the queue.");
                continue;
            }

            byte[] rowVersion;
            try
            {
                rowVersion = Convert.FromBase64String(link.EntryRowVersion ?? string.Empty);
                if (rowVersion.Length == 0) throw new FormatException();
            }
            catch (FormatException)
            {
                AddError(errors, $"Entries[{entry.Id}]", "The row version is invalid. Reload the queue.");
                continue;
            }

            _db.Entry(entry).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        }

        var proposedByIssue = entries
            .Where(entry => !errors.ContainsKey($"Entries[{entry.Id}]"))
            .GroupBy(entry => entry.ArppIssueId);

        foreach (var issueGroup in proposedByIssue)
        {
            var proposedProjectIds = issueGroup
                .Select(entry => linkByEntry[entry.Id].ProjectId)
                .ToArray();

            foreach (var duplicate in proposedProjectIds.GroupBy(id => id).Where(group => group.Count() > 1))
            {
                foreach (var entry in issueGroup.Where(item => linkByEntry[item.Id].ProjectId == duplicate.Key))
                {
                    AddError(errors, $"Entries[{entry.Id}]", "The same PRISM project cannot be linked twice in one ARPP issue.");
                }
            }

            var existingProjectIds = await _db.ArppEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.ArppIssueId == issueGroup.Key &&
                    entry.ProjectId.HasValue &&
                    proposedProjectIds.Contains(entry.ProjectId.Value))
                .Select(entry => entry.ProjectId!.Value)
                .ToListAsync(cancellationToken);

            foreach (var entry in issueGroup.Where(item => existingProjectIds.Contains(linkByEntry[item.Id].ProjectId)))
            {
                AddError(errors, $"Entries[{entry.Id}]", "The selected PRISM project is already linked elsewhere in this issue.");
            }
        }

        if (errors.Count > 0)
        {
            return ArppCommandResult.Failed("Review the selected project links.", errors);
        }

        var now = _clock.UtcNow.ToUniversalTime();
        foreach (var entry in entries)
        {
            entry.ProjectId = linkByEntry[entry.Id].ProjectId;
            entry.UpdatedAtUtc = now;
            entry.UpdatedByUserId = command.UserId;
            entry.Issue.UpdatedAtUtc = now;
            entry.Issue.UpdatedByUserId = command.UserId;
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "One or more ARPP rows changed while linking. Reload the reconciliation queue and review the latest data.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
            exception.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "A selected project is already linked in the same ARPP issue. Reload and review the affected row.");
        }

        transaction.RegisterAfterCommit(ct => _audit.LogAsync(
            action: "Arpp.EntriesReconciled",
            message: $"Linked {entries.Count} ARPP {Pluralize(entries.Count, "row", "rows")} to PRISM projects.",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["EntryIds"] = string.Join(",", entries.Select(entry => entry.Id)),
                ["ProjectIds"] = string.Join(",", entries.Select(entry => entry.ProjectId)),
                ["IssueIds"] = string.Join(",", entries.Select(entry => entry.ArppIssueId).Distinct())
            }));

        await transaction.CommitAsync(cancellationToken);
        return ArppCommandResult.Succeeded(
            entries.First().ArppIssueId,
            $"Linked {entries.Count} ARPP {Pluralize(entries.Count, "row", "rows")} to PRISM projects.");
    }

    private static decimal MatchScore(string source, string candidateName, string? caseFileNumber)
    {
        var sourceNormalized = Normalize(source);
        var nameNormalized = Normalize(candidateName);
        if (sourceNormalized.Length == 0 || nameNormalized.Length == 0) return 0m;
        if (sourceNormalized == nameNormalized) return 1m;

        var score = 0m;
        if (sourceNormalized.Contains(nameNormalized, StringComparison.Ordinal) ||
            nameNormalized.Contains(sourceNormalized, StringComparison.Ordinal))
        {
            score = 0.82m;
        }

        var sourceTokens = sourceNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var candidateTokens = nameNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var union = sourceTokens.Union(candidateTokens).Count();
        if (union > 0)
        {
            var jaccard = (decimal)sourceTokens.Intersect(candidateTokens).Count() / union;
            score = Math.Max(score, jaccard * 0.78m);
        }

        if (!string.IsNullOrWhiteSpace(caseFileNumber) &&
            sourceNormalized.Contains(Normalize(caseFileNumber), StringComparison.Ordinal))
        {
            score = Math.Max(score, 0.90m);
        }

        return Math.Min(1m, score);
    }

    private static string Normalize(string? value)
        => MultiSpaceRegex().Replace(
                NonAlphaNumericRegex().Replace((value ?? string.Empty).ToLowerInvariant(), " "),
                " ")
            .Trim();

    private static string Pluralize(int count, string singular, string plural)
        => count == 1 ? singular : plural;

    private static void AddError(
        IDictionary<string, IReadOnlyList<string>> errors,
        string field,
        string message)
    {
        if (errors.TryGetValue(field, out var existing))
        {
            errors[field] = existing.Concat(new[] { message }).Distinct().ToArray();
        }
        else
        {
            errors[field] = new[] { message };
        }
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpaceRegex();

    private sealed record CandidateProject(
        int Id,
        string Name,
        string? CaseFileNumber,
        string StatusLabel);
}
