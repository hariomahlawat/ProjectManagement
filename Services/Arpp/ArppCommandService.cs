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
    private const int EarlyIssueToleranceDays = 180;
    private const int LateIssueToleranceDays = 90;

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IArppIpaStageSynchronizer _ipaStageSynchronizer;

    public ArppCommandService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit)
        : this(db, clock, audit, new ArppIpaStageSynchronizer(db))
    {
    }

    public ArppCommandService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        IArppIpaStageSynchronizer ipaStageSynchronizer)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _ipaStageSynchronizer = ipaStageSynchronizer ?? throw new ArgumentNullException(nameof(ipaStageSynchronizer));
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

        var warnings = await BuildIssueWarningsAsync(
            command.FinancialYearStart,
            command.Kind,
            command.IssueSequence,
            command.IssueDate,
            excludedIssueId: null,
            cancellationToken);

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
                    ["Choose a different addendum number for this financial year."]
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
            warnings);
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

        var issueRowVersion = ParseRowVersion(
            command.IssueRowVersion,
            nameof(command.IssueRowVersion),
            errors);

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
                "Another ARPP issue already uses this addendum number in the selected financial year.");
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

        var referenceData = await ResolveReferenceDataAsync(command.Entries, errors, cancellationToken);

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

        if (issue.IsVerified)
        {
            return ArppCommandResult.Failed(
                "This ARPP issue is verified and locked. It must be unlocked with a recorded reason before its issued data can be changed.");
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

                var rowErrors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                var rowVersion = ParseRowVersion(
                    input.RowVersion,
                    $"Entries[{index}].RowVersion",
                    rowErrors);
                if (rowErrors.Count > 0)
                {
                    return ArppCommandResult.Failed(
                        "An ARPP row version is invalid. Reload the page before saving.",
                        rowErrors);
                }

                _db.Entry(entity).Property(entry => entry.RowVersion).OriginalValue = rowVersion;
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

            ApplyEntry(entity, input, index + 1, now, command.UserId, referenceData);
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
                "The ARPP issue could not be saved because an addendum number or linked project is duplicated.");
        }

        transaction.RegisterAfterCommit(ct => _audit.LogAsync(
            action: "Arpp.WorkspaceSaved",
            message: $"Saved {issue.Name} with {command.Entries.Count} {Pluralize(command.Entries.Count, "row", "rows")}.",
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
            command.Entries.Count == 0
                ? "ARPP issue saved with no rows. Rows may be added later."
                : "ARPP issue and rows saved.",
            warnings);
    }

    public async Task<ArppCommandResult> VerifyAsync(
        ArppVerifyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return ArppCommandResult.Failed("The current user could not be identified.");
        }

        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var rowVersion = ParseRowVersion(command.IssueRowVersion, nameof(command.IssueRowVersion), errors);
        if ((command.Note?.Trim().Length ?? 0) > 500)
        {
            AddError(errors, nameof(command.Note), "The verification note cannot exceed 500 characters.");
        }

        if (errors.Count > 0)
        {
            return ArppCommandResult.Failed("The issue could not be verified.", errors);
        }

        var issue = await _db.ArppIssues
            .Include(candidate => candidate.Entries)
            .Include(candidate => candidate.Attachment)
            .Include(candidate => candidate.PublishedSnapshot)
                .ThenInclude(snapshot => snapshot!.Entries)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.IssueId, cancellationToken);

        if (issue is null)
        {
            return ArppCommandResult.Failed("The ARPP issue was not found.");
        }

        if (issue.IsVerified)
        {
            return ArppCommandResult.Succeeded(issue.Id, "The ARPP issue is already verified and locked.");
        }

        if (issue.Entries.Count == 0)
        {
            AddError(errors, "Entries", "Enter at least one issued row before verification.");
        }

        if (issue.Attachment is null)
        {
            AddError(errors, "Attachment", "Attach the issued HQ PDF before verification.");
        }

        var unresolvedReferenceRows = issue.Entries.Count(entry =>
            !entry.CfaOptionId.HasValue ||
            !entry.FundOptionId.HasValue ||
            !entry.DfpdsScheduleId.HasValue);
        if (unresolvedReferenceRows > 0)
        {
            AddError(
                errors,
                "ReferenceData",
                $"Map CFA, Fund and DFPDS values for {unresolvedReferenceRows} {Pluralize(unresolvedReferenceRows, "row", "rows")} before verification.");
        }

        if (errors.Count > 0)
        {
            return ArppCommandResult.Failed("Complete the issued record before verification.", errors);
        }

        _db.Entry(issue).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        var now = _clock.UtcNow.ToUniversalTime();
        issue.IsVerified = true;
        issue.VerifiedAtUtc = now;
        issue.VerifiedByUserId = command.UserId;
        issue.VerificationNote = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        issue.UpdatedAtUtc = now;
        issue.UpdatedByUserId = command.UserId;

        // Publish a separate, organisation-visible snapshot. Unlocking the management
        // workspace never changes this snapshot; re-verification replaces it atomically.
        var published = issue.PublishedSnapshot;
        var previouslyPublishedProjectIds = published?.Entries
            .Where(entry => entry.ProjectId.HasValue)
            .Select(entry => entry.ProjectId!.Value)
            .Distinct()
            .ToArray() ?? [];
        var replacingPublishedEntries = published is not null && published.Entries.Count > 0;
        if (published is null)
        {
            published = new ArppPublishedIssue
            {
                ArppIssueId = issue.Id,
                RevisionNumber = 1
            };
            issue.PublishedSnapshot = published;
            _db.ArppPublishedIssues.Add(published);
        }
        else
        {
            published.RevisionNumber++;
            if (published.Entries.Count > 0)
            {
                _db.ArppPublishedEntries.RemoveRange(published.Entries);
                published.Entries.Clear();
            }
        }

        published.FinancialYearStart = issue.FinancialYearStart;
        published.Kind = issue.Kind;
        published.IssueSequence = issue.IssueSequence;
        published.Name = issue.Name;
        published.IssueDate = issue.IssueDate;
        published.PublishedAtUtc = now;
        published.PublishedByUserId = command.UserId;
        published.AttachmentStorageKey = issue.Attachment!.StorageKey;
        published.AttachmentOriginalFileName = issue.Attachment.OriginalFileName;
        published.AttachmentContentType = issue.Attachment.ContentType;
        published.AttachmentSizeBytes = issue.Attachment.SizeBytes;
        published.AttachmentSha256 = issue.Attachment.Sha256;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            // PostgreSQL enforces the unique published issue/project key immediately.
            // Delete the previous immutable rows first, inside the same transaction,
            // before inserting the replacement snapshot rows.
            if (replacingPublishedEntries)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            foreach (var entry in issue.Entries.OrderBy(entry => entry.SortOrder).ThenBy(entry => entry.Id))
            {
                published.Entries.Add(new ArppPublishedEntry
                {
                    ArppIssueId = issue.Id,
                    SourceEntryId = entry.Id,
                    SortOrder = entry.SortOrder,
                    SerialNumber = entry.SerialNumber,
                    ProjectReference = entry.ProjectReference,
                    ProjectId = entry.ProjectId,
                    Category = entry.Category,
                    IpaCost = entry.IpaCost,
                    Cfa = entry.Cfa,
                    Fund = entry.Fund,
                    DfpdsSchedule = entry.DfpdsSchedule
                });
            }

            await _db.SaveChangesAsync(cancellationToken);

            var affectedProjectIds = previouslyPublishedProjectIds
                .Concat(issue.Entries
                    .Where(entry => entry.ProjectId.HasValue)
                    .Select(entry => entry.ProjectId!.Value))
                .Distinct()
                .ToArray();

            var stageSynchronization = await _ipaStageSynchronizer.SynchronizeProjectsAsync(
                affectedProjectIds,
                cancellationToken);

            ArppIpaStageSynchronizationAudit.Register(
                transaction,
                _audit,
                stageSynchronization,
                command.UserId,
                command.UserName,
                sourceAction: "Arpp.IssueVerified",
                sourceIssueIds: [issue.Id]);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "The ARPP issue changed before verification. Reload it and verify the latest version.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "The ARPP issue could not be published because the linked project lifecycle could not be synchronized. Reload and try again.");
        }

        transaction.RegisterAfterCommit(ct => _audit.LogAsync(
            action: "Arpp.IssueVerified",
            message: $"Verified and locked {issue.Name}.",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["IssueId"] = issue.Id.ToString(),
                ["FinancialYear"] = FinancialYearHelper.Format(issue.FinancialYearStart),
                ["IssueSequence"] = issue.IssueSequence.ToString(),
                ["VerificationNote"] = issue.VerificationNote,
                ["PublishedRevision"] = published.RevisionNumber.ToString()
            }));

        await transaction.CommitAsync(cancellationToken);
        return ArppCommandResult.Succeeded(issue.Id, "ARPP issue verified and locked.");
    }

    public async Task<ArppCommandResult> UnlockAsync(
        ArppUnlockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            AddError(errors, nameof(command.UserId), "The current user could not be identified.");
        }

        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 10)
        {
            AddError(errors, nameof(command.Reason), "Enter a clear unlock reason of at least 10 characters.");
        }
        else if (reason.Length > 500)
        {
            AddError(errors, nameof(command.Reason), "The unlock reason cannot exceed 500 characters.");
        }

        var rowVersion = ParseRowVersion(command.IssueRowVersion, nameof(command.IssueRowVersion), errors);
        if (errors.Count > 0)
        {
            return ArppCommandResult.Failed("The issue could not be unlocked.", errors);
        }

        var issue = await _db.ArppIssues
            .SingleOrDefaultAsync(candidate => candidate.Id == command.IssueId, cancellationToken);

        if (issue is null)
        {
            return ArppCommandResult.Failed("The ARPP issue was not found.");
        }

        if (!issue.IsVerified)
        {
            return ArppCommandResult.Succeeded(issue.Id, "The ARPP issue is already unlocked.");
        }

        _db.Entry(issue).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        var now = _clock.UtcNow.ToUniversalTime();
        var previousVerifiedAt = issue.VerifiedAtUtc;
        var previousVerifiedBy = issue.VerifiedByUserId;
        issue.IsVerified = false;
        issue.VerifiedAtUtc = null;
        issue.VerifiedByUserId = null;
        issue.VerificationNote = null;
        issue.UpdatedAtUtc = now;
        issue.UpdatedByUserId = command.UserId;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ArppCommandResult.Failed(
                "The ARPP issue changed before it could be unlocked. Reload it and try again.");
        }

        transaction.RegisterAfterCommit(ct => _audit.LogAsync(
            action: "Arpp.IssueUnlocked",
            message: $"Unlocked {issue.Name} for correction.",
            userId: command.UserId,
            userName: command.UserName,
            data: new Dictionary<string, string?>
            {
                ["IssueId"] = issue.Id.ToString(),
                ["FinancialYear"] = FinancialYearHelper.Format(issue.FinancialYearStart),
                ["IssueSequence"] = issue.IssueSequence.ToString(),
                ["Reason"] = reason,
                ["PreviouslyVerifiedAtUtc"] = previousVerifiedAt?.ToString("O"),
                ["PreviouslyVerifiedByUserId"] = previousVerifiedBy
            }));

        await transaction.CommitAsync(cancellationToken);
        return ArppCommandResult.Succeeded(issue.Id, "ARPP issue unlocked. Corrections are now permitted and will be audited.");
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
            AddError(errors, nameof(financialYearStart), "Select a valid financial year.");
        }

        if (!Enum.IsDefined(kind))
        {
            AddError(errors, nameof(kind), "Select Original ARPP or Addendum.");
        }

        if (kind == ArppIssueKind.Original && issueSequence != 0)
        {
            AddError(errors, nameof(issueSequence), "The original ARPP must use internal sequence 0.");
        }

        if (kind == ArppIssueKind.Addendum && issueSequence <= 0)
        {
            AddError(errors, nameof(issueSequence), "Enter a valid addendum number.");
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

        foreach (var group in indexedEntries
                     .Where(item => item.Entry.Id.HasValue)
                     .GroupBy(item => item.Entry.Id!.Value)
                     .Where(group => group.Count() > 1))
        {
            foreach (var item in group)
            {
                AddError(
                    errors,
                    $"Entries[{item.Index}].Id",
                    "The same saved ARPP row was submitted more than once. Reload the page.");
            }
        }

        foreach (var group in indexedEntries
                     .Where(item => item.Entry.ProjectId.HasValue)
                     .GroupBy(item => item.Entry.ProjectId!.Value)
                     .Where(group => group.Count() > 1))
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
        warnings.AddRange(await BuildIssueWarningsAsync(
            command.FinancialYearStart,
            command.Kind,
            command.IssueSequence,
            command.IssueDate,
            command.IssueId,
            cancellationToken));

        var unlinkedRows = command.Entries.Count(entry => !entry.ProjectId.HasValue);
        if (unlinkedRows > 0)
        {
            warnings.Add($"{unlinkedRows} {Pluralize(unlinkedRows, "row remains", "rows remain")} unlinked to a PRISM project. The issued data was saved and can be reconciled later.");
        }

        var unresolvedReferenceRows = command.Entries.Count(entry =>
            !entry.CfaOptionId.HasValue ||
            !entry.FundOptionId.HasValue ||
            !entry.DfpdsScheduleId.HasValue);
        if (unresolvedReferenceRows > 0)
        {
            warnings.Add($"{unresolvedReferenceRows} {Pluralize(unresolvedReferenceRows, "row has", "rows have")} reference values entered exactly as issued but not yet mapped to Admin-controlled CFA, Fund or DFPDS lists. Verification remains unavailable until mapping is complete.");
        }

        var zeroCostRows = command.Entries.Count(entry => entry.IpaCost == 0m);
        if (zeroCostRows > 0)
        {
            warnings.Add($"{zeroCostRows} {Pluralize(zeroCostRows, "row has", "rows have")} an IPA cost of zero. The value was retained as entered.");
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
            return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

    private async Task<IReadOnlyList<string>> BuildIssueWarningsAsync(
        int financialYearStart,
        ArppIssueKind kind,
        int issueSequence,
        DateOnly issueDate,
        long? excludedIssueId,
        CancellationToken cancellationToken)
    {
        if (issueDate == default ||
            financialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            return Array.Empty<string>();
        }

        var warnings = new List<string>();
        var normalWindowStart = FinancialYearHelper.GetStartDate(financialYearStart).AddDays(-EarlyIssueToleranceDays);
        var normalWindowEnd = FinancialYearHelper.GetEndDate(financialYearStart).AddDays(LateIssueToleranceDays);

        if (issueDate < normalWindowStart || issueDate > normalWindowEnd)
        {
            warnings.Add(
                $"The issue date {issueDate:dd MMM yyyy} is unusually distant from FY {FinancialYearHelper.Format(financialYearStart)}. Verify the financial year and document date.");
        }

        var sameYearIssues = await _db.ArppIssues
            .AsNoTracking()
            .Where(issue =>
                issue.FinancialYearStart == financialYearStart &&
                (!excludedIssueId.HasValue || issue.Id != excludedIssueId.Value))
            .Select(issue => new { issue.IssueSequence, issue.IssueDate, issue.Name })
            .ToListAsync(cancellationToken);

        if (kind == ArppIssueKind.Addendum)
        {
            var original = sameYearIssues.FirstOrDefault(issue => issue.IssueSequence == 0);
            if (original is null)
            {
                warnings.Add($"No original ARPP is recorded for FY {FinancialYearHelper.Format(financialYearStart)}.");
            }
            else if (issueDate < original.IssueDate)
            {
                warnings.Add(
                    $"The addendum date {issueDate:dd MMM yyyy} precedes the original ARPP dated {original.IssueDate:dd MMM yyyy}.");
            }
        }

        var previous = sameYearIssues
            .Where(issue => issue.IssueSequence < issueSequence)
            .OrderByDescending(issue => issue.IssueSequence)
            .FirstOrDefault();
        if (previous is not null && issueDate < previous.IssueDate)
        {
            warnings.Add(
                $"The issue date precedes {previous.Name} dated {previous.IssueDate:dd MMM yyyy}. Verify the addendum chronology.");
        }

        var next = sameYearIssues
            .Where(issue => issue.IssueSequence > issueSequence)
            .OrderBy(issue => issue.IssueSequence)
            .FirstOrDefault();
        if (next is not null && issueDate > next.IssueDate)
        {
            warnings.Add(
                $"The issue date follows {next.Name} dated {next.IssueDate:dd MMM yyyy}. Verify the addendum chronology.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static byte[] ParseRowVersion(
        string? value,
        string field,
        IDictionary<string, IReadOnlyList<string>> errors)
    {
        try
        {
            var rowVersion = Convert.FromBase64String(value ?? string.Empty);
            if (rowVersion.Length == 0)
            {
                AddError(errors, field, "The record version is missing. Reload the page.");
            }

            return rowVersion;
        }
        catch (FormatException)
        {
            AddError(errors, field, "The record version is invalid. Reload the page.");
            return Array.Empty<byte>();
        }
    }

    private static void ApplyEntry(
        ArppEntry entity,
        ArppEntryInput input,
        int sortOrder,
        DateTimeOffset now,
        string userId,
        ResolvedReferenceData referenceData)
    {
        entity.SortOrder = sortOrder;
        entity.SerialNumber = input.SerialNumber.Trim();
        entity.ProjectReference = input.ProjectReference.Trim();
        entity.ProjectId = input.ProjectId;
        entity.Category = input.Category!.Value;
        entity.IpaCost = input.IpaCost!.Value;

        ApplyReferenceSnapshot(
            entity.CfaOptionId,
            entity.Cfa,
            input.CfaOptionId,
            input.Cfa,
            referenceData.Cfa,
            out var cfaOptionId,
            out var cfaSnapshot);
        entity.CfaOptionId = cfaOptionId;
        entity.Cfa = cfaSnapshot;

        ApplyReferenceSnapshot(
            entity.FundOptionId,
            entity.Fund,
            input.FundOptionId,
            input.Fund,
            referenceData.Fund,
            out var fundOptionId,
            out var fundSnapshot);
        entity.FundOptionId = fundOptionId;
        entity.Fund = fundSnapshot;

        ApplyReferenceSnapshot(
            entity.DfpdsScheduleId,
            entity.DfpdsSchedule,
            input.DfpdsScheduleId,
            input.DfpdsSchedule,
            referenceData.Dfpds,
            out var dfpdsScheduleId,
            out var dfpdsSnapshot);
        entity.DfpdsScheduleId = dfpdsScheduleId;
        entity.DfpdsSchedule = dfpdsSnapshot;

        entity.UpdatedAtUtc = now;
        entity.UpdatedByUserId = userId;
    }

    private static void ApplyReferenceSnapshot(
        int? existingOptionId,
        string existingSnapshot,
        int? submittedOptionId,
        string submittedSnapshot,
        IReadOnlyDictionary<int, string> options,
        out int? optionId,
        out string snapshot)
    {
        optionId = submittedOptionId is > 0 ? submittedOptionId : null;
        if (!optionId.HasValue)
        {
            snapshot = submittedSnapshot.Trim();
            return;
        }

        if (existingOptionId == optionId && !string.IsNullOrWhiteSpace(existingSnapshot))
        {
            snapshot = existingSnapshot;
            return;
        }

        snapshot = options[optionId.Value];
    }

    private async Task<ResolvedReferenceData> ResolveReferenceDataAsync(
        IReadOnlyList<ArppEntryInput> entries,
        IDictionary<string, IReadOnlyList<string>> errors,
        CancellationToken cancellationToken)
    {
        var cfaIds = entries.Where(item => item.CfaOptionId is > 0).Select(item => item.CfaOptionId!.Value).Distinct().ToArray();
        var fundIds = entries.Where(item => item.FundOptionId is > 0).Select(item => item.FundOptionId!.Value).Distinct().ToArray();
        var dfpdsIds = entries.Where(item => item.DfpdsScheduleId is > 0).Select(item => item.DfpdsScheduleId!.Value).Distinct().ToArray();
        var savedEntryIds = entries.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).Distinct().ToArray();

        var cfaRows = await _db.ArppCfaOptions.AsNoTracking()
            .Where(item => cfaIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name, item.IsActive })
            .ToListAsync(cancellationToken);
        var fundRows = await _db.ArppFundOptions.AsNoTracking()
            .Where(item => fundIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name, item.IsActive })
            .ToListAsync(cancellationToken);
        var dfpdsRows = await _db.ArppDfpdsSchedules.AsNoTracking()
            .Where(item => dfpdsIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Code, item.IsActive })
            .ToListAsync(cancellationToken);
        var existingSelections = await _db.ArppEntries.AsNoTracking()
            .Where(item => savedEntryIds.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.CfaOptionId,
                item.FundOptionId,
                item.DfpdsScheduleId
            })
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var cfa = cfaRows.ToDictionary(item => item.Id, item => item.Name);
        var fund = fundRows.ToDictionary(item => item.Id, item => item.Name);
        var dfpds = dfpdsRows.ToDictionary(item => item.Id, item => item.Code);
        var inactiveCfa = cfaRows.Where(item => !item.IsActive).Select(item => item.Id).ToHashSet();
        var inactiveFund = fundRows.Where(item => !item.IsActive).Select(item => item.Id).ToHashSet();
        var inactiveDfpds = dfpdsRows.Where(item => !item.IsActive).Select(item => item.Id).ToHashSet();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            existingSelections.TryGetValue(entry.Id ?? 0, out var existing);

            if (entry.CfaOptionId is > 0 && !cfa.ContainsKey(entry.CfaOptionId.Value))
            {
                AddError(errors, $"Entries[{index}].CfaOptionId", "The selected CFA value is no longer available.");
            }
            else if (entry.CfaOptionId is > 0 && inactiveCfa.Contains(entry.CfaOptionId.Value) && existing?.CfaOptionId != entry.CfaOptionId)
            {
                AddError(errors, $"Entries[{index}].CfaOptionId", "The selected CFA value is inactive. Choose an active value.");
            }

            if (entry.FundOptionId is > 0 && !fund.ContainsKey(entry.FundOptionId.Value))
            {
                AddError(errors, $"Entries[{index}].FundOptionId", "The selected Fund value is no longer available.");
            }
            else if (entry.FundOptionId is > 0 && inactiveFund.Contains(entry.FundOptionId.Value) && existing?.FundOptionId != entry.FundOptionId)
            {
                AddError(errors, $"Entries[{index}].FundOptionId", "The selected Fund value is inactive. Choose an active value.");
            }

            if (entry.DfpdsScheduleId is > 0 && !dfpds.ContainsKey(entry.DfpdsScheduleId.Value))
            {
                AddError(errors, $"Entries[{index}].DfpdsScheduleId", "The selected DFPDS schedule is no longer available.");
            }
            else if (entry.DfpdsScheduleId is > 0 && inactiveDfpds.Contains(entry.DfpdsScheduleId.Value) && existing?.DfpdsScheduleId != entry.DfpdsScheduleId)
            {
                AddError(errors, $"Entries[{index}].DfpdsScheduleId", "The selected DFPDS schedule is inactive. Choose an active value.");
            }
        }

        return new ResolvedReferenceData(cfa, fund, dfpds);
    }

    private sealed record ResolvedReferenceData(
        IReadOnlyDictionary<int, string> Cfa,
        IReadOnlyDictionary<int, string> Fund,
        IReadOnlyDictionary<int, string> Dfpds);

    private static string Normalize(string? value)
        => string.Join(' ', (value ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
            return;
        }

        errors[field] = new[] { message };
    }

    private static bool IsLikelyUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
           exception.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);
}
