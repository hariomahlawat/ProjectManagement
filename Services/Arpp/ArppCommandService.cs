using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppCommandService : IArppCommandService
{
    private const decimal SignificantCostChangeRatio = 0.25m;

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ArppCommandService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ArppCommandResult> CreateIssueAsync(
        ArppIssueCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = ValidateIssue(
            command.FinancialYearStart,
            command.Kind,
            command.IssueSequence,
            command.Name,
            command.IssueDate);

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            AddError(errors, nameof(command.UserId), "The current user could not be identified.");
        }

        if (await _db.ArppIssues.AsNoTracking().AnyAsync(
                issue => issue.FinancialYearStart == command.FinancialYearStart &&
                         issue.IssueSequence == command.IssueSequence,
                cancellationToken))
        {
            AddError(
                errors,
                nameof(command.IssueSequence),
                "An ARPP issue with this financial year and sequence already exists.");
        }

        if (errors.Count > 0)
        {
            return ArppCommandResult.Failed("Review the highlighted ARPP issue fields.", errors);
        }

        var now = _clock.UtcNow.ToUniversalTime();
        var issue = new ArppIssue
        {
            FinancialYearStart = command.FinancialYearStart,
            Kind = command.Kind,
            IssueSequence = command.IssueSequence,
            Name = command.Name.Trim(),
            IssueDate = command.IssueDate,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = command.UserId,
            UpdatedByUserId = command.UserId
        };

        await using var transaction = await RelationalTransactionScope.CreateAsync(
            _db.Database,
            cancellationToken);

        _db.ArppIssues.Add(issue);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsLikelyUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "An ARPP issue with this financial year and sequence already exists.",
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(command.IssueSequence)] =
                    ["Choose a different issue sequence for this financial year."]
                });
        }

        transaction.RegisterAfterCommit(ct => _audit.LogAsync(
            action: "Arpp.IssueCreated",
            message: $"Created {ArppDisplayNames.For(issue.Kind)} for FY {FinancialYearHelper.Format(issue.FinancialYearStart)}.",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["IssueId"] = issue.Id.ToString(),
                ["FinancialYear"] = FinancialYearHelper.Format(issue.FinancialYearStart),
                ["IssueSequence"] = issue.IssueSequence.ToString(),
                ["IssueName"] = issue.Name
            }));

        await transaction.CommitAsync(cancellationToken);

        return ArppCommandResult.Succeeded(
            issue.Id,
            "ARPP issue created. Add the issued rows in the workspace.",
            BuildIssueWarnings(issue.FinancialYearStart, issue.IssueDate));
    }

    public async Task<ArppCommandResult> SaveWorkspaceAsync(
        ArppWorkspaceSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.IssueId <= 0)
        {
            return ArppCommandResult.Failed("A valid ARPP issue is required.");
        }

        var errors = ValidateIssue(
            command.FinancialYearStart,
            command.Kind,
            command.IssueSequence,
            command.Name,
            command.IssueDate);

        ValidateEntries(command.Entries, errors);

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            AddError(errors, nameof(command.UserId), "The current user could not be identified.");
        }

        byte[] issueRowVersion;
        try
        {
            issueRowVersion = Convert.FromBase64String(command.IssueRowVersion ?? string.Empty);
            if (issueRowVersion.Length == 0)
            {
                AddError(errors, nameof(command.IssueRowVersion), "The ARPP record version is missing. Reload the page.");
            }
        }
        catch (FormatException)
        {
            AddError(errors, nameof(command.IssueRowVersion), "The ARPP record version is invalid. Reload the page.");
            issueRowVersion = Array.Empty<byte>();
        }

        var duplicateSequence = await _db.ArppIssues
            .AsNoTracking()
            .AnyAsync(
                issue => issue.Id != command.IssueId &&
                         issue.FinancialYearStart == command.FinancialYearStart &&
                         issue.IssueSequence == command.IssueSequence,
                cancellationToken);

        if (duplicateSequence)
        {
            AddError(
                errors,
                nameof(command.IssueSequence),
                "Another ARPP issue already uses this sequence in the selected financial year.");
        }

        var linkedProjectIds = command.Entries
            .Where(entry => entry.ProjectId.HasValue)
            .Select(entry => entry.ProjectId!.Value)
            .Distinct()
            .ToArray();

        if (linkedProjectIds.Length > 0)
        {
            var validProjectIds = await _db.Projects
                .AsNoTracking()
                .Where(project => linkedProjectIds.Contains(project.Id) && !project.IsDeleted)
                .Select(project => project.Id)
                .ToListAsync(cancellationToken);

            var missingIds = linkedProjectIds.Except(validProjectIds).ToArray();
            foreach (var missingId in missingIds)
            {
                for (var index = 0; index < command.Entries.Count; index++)
                {
                    if (command.Entries[index].ProjectId == missingId)
                    {
                        AddError(
                            errors,
                            $"Entries[{index}].ProjectId",
                            "The linked PRISM project is no longer available. Clear it or select another project.");
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            return ArppCommandResult.Failed("Review the highlighted ARPP fields.", errors);
        }

        var issue = await _db.ArppIssues
            .Include(candidate => candidate.Entries)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.IssueId, cancellationToken);

        if (issue is null)
        {
            return ArppCommandResult.Failed("The ARPP issue no longer exists.");
        }

        _db.Entry(issue).Property(candidate => candidate.RowVersion).OriginalValue = issueRowVersion;

        var submittedIds = command.Entries
            .Where(entry => entry.Id.HasValue)
            .Select(entry => entry.Id!.Value)
            .ToHashSet();

        var unknownEntryIds = submittedIds
            .Except(issue.Entries.Select(entry => entry.Id))
            .ToArray();

        if (unknownEntryIds.Length > 0)
        {
            return ArppCommandResult.Failed(
                "One or more ARPP rows no longer belong to this issue. Reload the page before saving.");
        }

        var warnings = await BuildWorkspaceWarningsAsync(command, cancellationToken);
        var now = _clock.UtcNow.ToUniversalTime();

        issue.FinancialYearStart = command.FinancialYearStart;
        issue.Kind = command.Kind;
        issue.IssueSequence = command.IssueSequence;
        issue.Name = command.Name.Trim();
        issue.IssueDate = command.IssueDate;
        issue.UpdatedAtUtc = now;
        issue.UpdatedByUserId = command.UserId;

        var existingById = issue.Entries.ToDictionary(entry => entry.Id);
        var retainedIds = new HashSet<long>();

        for (var index = 0; index < command.Entries.Count; index++)
        {
            var input = command.Entries[index];
            ArppEntry entity;

            if (input.Id.HasValue)
            {
                entity = existingById[input.Id.Value];
                retainedIds.Add(entity.Id);

                if (!string.IsNullOrWhiteSpace(input.RowVersion))
                {
                    try
                    {
                        _db.Entry(entity).Property(entry => entry.RowVersion).OriginalValue =
                            Convert.FromBase64String(input.RowVersion);
                    }
                    catch (FormatException)
                    {
                        return ArppCommandResult.Failed(
                            "An ARPP row version is invalid. Reload the page before saving.");
                    }
                }
            }
            else
            {
                entity = new ArppEntry
                {
                    CreatedAtUtc = now,
                    CreatedByUserId = command.UserId
                };
                issue.Entries.Add(entity);
            }

            ApplyEntry(entity, input, index + 1, now, command.UserId);
        }

        foreach (var obsolete in issue.Entries
                     .Where(entry => entry.Id > 0 && !retainedIds.Contains(entry.Id))
                     .ToArray())
        {
            _db.ArppEntries.Remove(obsolete);
        }

        await using var transaction = await RelationalTransactionScope.CreateAsync(
            _db.Database,
            cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "This ARPP issue was changed by another user. Reload the page and review the latest version before saving again.");
        }
        catch (DbUpdateException exception) when (IsLikelyUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "The ARPP issue could not be saved because an issue sequence or linked project is duplicated.");
        }

        transaction.RegisterAfterCommit(ct => _audit.LogAsync(
            action: "Arpp.WorkspaceSaved",
            message: $"Saved {issue.Name} with {command.Entries.Count} row(s).",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["IssueId"] = issue.Id.ToString(),
                ["FinancialYear"] = FinancialYearHelper.Format(issue.FinancialYearStart),
                ["IssueSequence"] = issue.IssueSequence.ToString(),
                ["EntryCount"] = command.Entries.Count.ToString(),
                ["LinkedProjectCount"] = linkedProjectIds.Length.ToString()
            }));

        await transaction.CommitAsync(cancellationToken);

        return ArppCommandResult.Succeeded(
            issue.Id,
            "ARPP issue and rows saved.",
            warnings);
    }

    private static Dictionary<string, IReadOnlyList<string>> ValidateIssue(
        int financialYearStart,
        ArppIssueKind kind,
        int issueSequence,
        string name,
        DateOnly issueDate)
    {
        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (financialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            AddError(errors, nameof(financialYearStart), "Enter a valid four-digit financial-year start.");
        }

        if (!Enum.IsDefined(kind))
        {
            AddError(errors, nameof(kind), "Select Original ARPP or Addendum.");
        }

        if (kind == ArppIssueKind.Original && issueSequence != 0)
        {
            AddError(errors, nameof(issueSequence), "The original ARPP must use issue sequence 0.");
        }

        if (kind == ArppIssueKind.Addendum && issueSequence <= 0)
        {
            AddError(errors, nameof(issueSequence), "An addendum must use an issue sequence greater than 0.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
        {
            AddError(errors, nameof(name), "Enter the issued document name or letter description.");
        }
        else if (normalizedName.Length > 300)
        {
            AddError(errors, nameof(name), "The issue name cannot exceed 300 characters.");
        }

        if (issueDate == default)
        {
            AddError(errors, nameof(issueDate), "Enter the issue date.");
        }

        return errors;
    }

    private static void ValidateEntries(
        IReadOnlyList<ArppEntryInput> entries,
        IDictionary<string, IReadOnlyList<string>> errors)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var indexedEntries = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .ToArray();

        var duplicateEntryIds = indexedEntries
            .Where(item => item.Entry.Id.HasValue)
            .GroupBy(item => item.Entry.Id!.Value)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateEntryIds)
        {
            foreach (var item in group)
            {
                AddError(
                    errors,
                    $"Entries[{item.Index}].Id",
                    "The same saved ARPP row was submitted more than once. Reload the page.");
            }
        }

        var linkedProjectGroups = indexedEntries
            .Where(item => item.Entry.ProjectId.HasValue)
            .GroupBy(item => item.Entry.ProjectId!.Value)
            .Where(group => group.Count() > 1);

        foreach (var group in linkedProjectGroups)
        {
            foreach (var item in group)
            {
                AddError(
                    errors,
                    $"Entries[{item.Index}].ProjectId",
                    "The same PRISM project cannot appear more than once in one ARPP issue.");
            }
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Id.HasValue && string.IsNullOrWhiteSpace(entry.RowVersion))
            {
                AddError(errors, $"Entries[{index}].RowVersion", "The saved row version is missing. Reload the page.");
            }

            ValidateRequiredText(errors, index, nameof(entry.SerialNumber), entry.SerialNumber, 64, "serial number");
            ValidateRequiredText(errors, index, nameof(entry.ProjectReference), entry.ProjectReference, 300, "project reference");
            ValidateRequiredText(errors, index, nameof(entry.Cfa), entry.Cfa, 200, "CFA");
            ValidateRequiredText(errors, index, nameof(entry.Fund), entry.Fund, 120, "fund");
            ValidateRequiredText(errors, index, nameof(entry.DfpdsSchedule), entry.DfpdsSchedule, 120, "DFPDS schedule");

            if (!entry.Category.HasValue || !Enum.IsDefined(entry.Category.Value))
            {
                AddError(errors, $"Entries[{index}].Category", "Select an ARPP category.");
            }

            if (!entry.IpaCost.HasValue)
            {
                AddError(errors, $"Entries[{index}].IpaCost", "Enter the IPA cost.");
            }
            else if (entry.IpaCost.Value < 0m)
            {
                AddError(errors, $"Entries[{index}].IpaCost", "IPA cost cannot be negative.");
            }
        }
    }

    private static void ValidateRequiredText(
        IDictionary<string, IReadOnlyList<string>> errors,
        int index,
        string property,
        string? value,
        int maximumLength,
        string displayName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            AddError(errors, $"Entries[{index}].{property}", $"Enter the {displayName}.");
        }
        else if (normalized.Length > maximumLength)
        {
            AddError(errors, $"Entries[{index}].{property}", $"The {displayName} cannot exceed {maximumLength} characters.");
        }
    }

    private async Task<IReadOnlyList<string>> BuildWorkspaceWarningsAsync(
        ArppWorkspaceSaveCommand command,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        warnings.AddRange(BuildIssueWarnings(command.FinancialYearStart, command.IssueDate));

        var unlinkedRows = command.Entries.Count(entry => !entry.ProjectId.HasValue);
        if (unlinkedRows > 0)
        {
            warnings.Add($"{unlinkedRows} row(s) remain unlinked to a PRISM project. The issued data was saved and can be linked later.");
        }

        var zeroCostRows = command.Entries.Count(entry => entry.IpaCost == 0m);
        if (zeroCostRows > 0)
        {
            warnings.Add($"{zeroCostRows} row(s) have an IPA cost of zero. The values were retained as entered.");
        }

        var duplicateSerials = command.Entries
            .GroupBy(entry => Normalize(entry.SerialNumber), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => group.First().SerialNumber.Trim())
            .ToArray();
        if (duplicateSerials.Length > 0)
        {
            warnings.Add($"Duplicate serial number(s) detected: {string.Join(", ", duplicateSerials)}. Verify these against the issued document.");
        }

        var duplicateUnlinkedReferences = command.Entries
            .Where(entry => !entry.ProjectId.HasValue)
            .GroupBy(entry => Normalize(entry.ProjectReference), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => group.First().ProjectReference.Trim())
            .ToArray();
        if (duplicateUnlinkedReferences.Length > 0)
        {
            warnings.Add($"Repeated unlinked project reference(s) detected: {string.Join(", ", duplicateUnlinkedReferences)}.");
        }

        var linkedInputs = command.Entries
            .Where(entry => entry.ProjectId.HasValue && entry.Category.HasValue && entry.IpaCost.HasValue)
            .ToArray();

        if (linkedInputs.Length == 0)
        {
            return warnings;
        }

        var projectIds = linkedInputs.Select(entry => entry.ProjectId!.Value).Distinct().ToArray();
        var priorRows = await _db.ArppEntries
            .AsNoTracking()
            .Where(entry =>
                entry.ArppIssueId != command.IssueId &&
                entry.ProjectId.HasValue &&
                projectIds.Contains(entry.ProjectId.Value) &&
                (entry.Issue.FinancialYearStart < command.FinancialYearStart ||
                 (entry.Issue.FinancialYearStart == command.FinancialYearStart &&
                  entry.Issue.IssueSequence < command.IssueSequence)))
            .Select(entry => new
            {
                ProjectId = entry.ProjectId!.Value,
                entry.IpaCost,
                entry.Category,
                entry.ProjectReference,
                entry.Issue.FinancialYearStart,
                entry.Issue.IssueSequence,
                entry.Issue.IssueDate,
                IssueId = entry.ArppIssueId,
                EntryId = entry.Id
            })
            .ToListAsync(cancellationToken);

        var latestPrior = priorRows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.FinancialYearStart)
                    .ThenByDescending(row => row.IssueSequence)
                    .ThenByDescending(row => row.IssueDate)
                    .ThenByDescending(row => row.IssueId)
                    .ThenByDescending(row => row.EntryId)
                    .First());

        foreach (var input in linkedInputs)
        {
            if (!latestPrior.TryGetValue(input.ProjectId!.Value, out var prior))
            {
                continue;
            }

            if (prior.IpaCost > 0m)
            {
                var ratio = Math.Abs(input.IpaCost!.Value - prior.IpaCost) / prior.IpaCost;
                if (ratio >= SignificantCostChangeRatio)
                {
                    warnings.Add(
                        $"{input.ProjectReference.Trim()}: IPA cost changes by {ratio:P0} from the latest earlier ARPP position.");
                }
            }

            if (prior.Category != input.Category!.Value &&
                (prior.Category == ArppCategory.Delisted || input.Category.Value == ArppCategory.Delisted))
            {
                warnings.Add(
                    $"{input.ProjectReference.Trim()}: category changes from {ArppDisplayNames.For(prior.Category)} to {ArppDisplayNames.For(input.Category.Value)}.");
            }
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildIssueWarnings(int financialYearStart, DateOnly issueDate)
    {
        if (issueDate == default ||
            financialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            return Array.Empty<string>();
        }

        return FinancialYearHelper.Contains(financialYearStart, issueDate)
            ? Array.Empty<string>()
            : [
                $"The issue date {issueDate:dd MMM yyyy} falls outside FY {FinancialYearHelper.Format(financialYearStart)}. The record was retained as entered."
            ];
    }

    private static void ApplyEntry(
        ArppEntry entity,
        ArppEntryInput input,
        int sortOrder,
        DateTimeOffset now,
        string userId)
    {
        entity.SortOrder = sortOrder;
        entity.SerialNumber = input.SerialNumber.Trim();
        entity.ProjectReference = input.ProjectReference.Trim();
        entity.ProjectId = input.ProjectId;
        entity.Category = input.Category!.Value;
        entity.IpaCost = input.IpaCost!.Value;
        entity.Cfa = input.Cfa.Trim();
        entity.Fund = input.Fund.Trim();
        entity.DfpdsSchedule = input.DfpdsSchedule.Trim();
        entity.UpdatedAtUtc = now;
        entity.UpdatedByUserId = userId;
    }

    private static string Normalize(string? value)
        => string.Join(' ', (value ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void AddError(
        IDictionary<string, IReadOnlyList<string>> errors,
        string field,
        string message)
    {
        if (errors.TryGetValue(field, out var existing))
        {
            errors[field] = existing.Concat(new[] { message }).Distinct().ToArray();
            return;
        }

        errors[field] = new[] { message };
    }

    private static bool IsLikelyUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
           exception.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);
}
