using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

public sealed record ArppIssueCreateCommand(
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    string UserId,
    string? UserName);

public sealed record ArppEntryInput(
    long? Id,
    string? RowVersion,
    string? SerialNumber,
    string? PppNumber,
    string ProjectReference,
    int? ProjectId,
    ArppCategory? Category,
    decimal? IpaCost,
    int? CfaOptionId,
    string Cfa,
    int? FundOptionId,
    string Fund,
    int? DfpdsScheduleId,
    string DfpdsSchedule);

public sealed record ArppWorkspaceSaveCommand(
    long IssueId,
    string IssueRowVersion,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    IReadOnlyList<ArppEntryInput> Entries,
    string UserId,
    string? UserName);

public sealed record ArppVerifyCommand(
    long IssueId,
    string IssueRowVersion,
    string? Note,
    string UserId,
    string? UserName);

public sealed record ArppUnlockCommand(
    long IssueId,
    string IssueRowVersion,
    string Reason,
    string UserId,
    string? UserName);

public sealed record ArppCommandResult(
    bool Success,
    long? EntityId,
    string? Message,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors,
    IReadOnlyList<string> Warnings)
{
    public static ArppCommandResult Succeeded(
        long entityId,
        string message,
        IReadOnlyList<string>? warnings = null)
        => new(
            true,
            entityId,
            message,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            warnings ?? Array.Empty<string>());

    public static ArppCommandResult Failed(
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fieldErrors = null)
        => new(
            false,
            null,
            message,
            fieldErrors ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<string>());
}

public sealed record ArppRegisterResult(
    IReadOnlyList<ArppFinancialYearGroup> FinancialYears,
    IReadOnlyList<int> AvailableFinancialYears,
    int TotalIssues,
    int TotalEntries,
    decimal ApprovedLinkedIpaCost,
    decimal DelistedLinkedIpaCost,
    decimal UnlinkedDocumentRowValue,
    int ApprovedLinkedProjects,
    int DelistedLinkedProjects,
    int UnlinkedEntries,
    int VerifiedIssues)
{
    // Compatibility aliases retained for downstream callers that need the complete latest
    // linked position. User-facing summaries should normally display the approved and
    // Delisted components separately.
    public decimal AuthoritativeLinkedIpaCost => ApprovedLinkedIpaCost + DelistedLinkedIpaCost;
    public decimal TotalIpaCost => AuthoritativeLinkedIpaCost;
    public int LinkedEntries => ApprovedLinkedProjects + DelistedLinkedProjects;
}

public sealed record ArppFinancialYearGroup(
    int FinancialYearStart,
    IReadOnlyList<ArppIssueListItem> Issues,
    int EntryCount,
    decimal ApprovedLinkedIpaCost,
    decimal DelistedLinkedIpaCost,
    decimal UnlinkedDocumentRowValue,
    int ApprovedLinkedProjectCount,
    int DelistedLinkedProjectCount,
    int UnlinkedEntryCount)
{
    public decimal AuthoritativeLinkedIpaCost => ApprovedLinkedIpaCost + DelistedLinkedIpaCost;
    public decimal TotalIpaCost => AuthoritativeLinkedIpaCost;
    public int LinkedProjectCount => ApprovedLinkedProjectCount + DelistedLinkedProjectCount;
}

public sealed record ArppIssueListItem(
    long Id,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    int EntryCount,
    decimal TotalIpaCost,
    decimal ApprovedRowValue,
    decimal DelistedRowValue,
    int NewCount,
    int CommittedLiabilityCount,
    int CarryForwardCount,
    int DelistedCount,
    int LinkedCount,
    int UnlinkedCount,
    DateTimeOffset UpdatedAtUtc,
    bool HasAttachment,
    bool IsVerified,
    DateTimeOffset? VerifiedAtUtc,
    bool HasPublishedSnapshot,
    int? PublishedRevisionNumber,
    bool HasUnresolvedReferenceData);

public sealed record ArppIssueDetails(
    long Id,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    string RowVersion,
    IReadOnlyList<ArppEntryDetails> Entries,
    decimal TotalIpaCost,
    IReadOnlyDictionary<ArppCategory, ArppCategorySummary> CategorySummary,
    int LinkedCount,
    int UnlinkedCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ArppAttachmentDetails? Attachment,
    bool IsVerified,
    DateTimeOffset? VerifiedAtUtc,
    string? VerifiedByUserId,
    string? VerifiedByDisplayName,
    string? VerificationNote)
{
    /// <summary>
    /// True when a last verified revision remains available in the organisation-wide
    /// published ARPP library. This can remain true while the management record is
    /// unlocked for correction.
    /// </summary>
    public bool HasPublishedSnapshot { get; init; }

    public int? PublishedRevisionNumber { get; init; }

    public DateTimeOffset? PublishedAtUtc { get; init; }
}

public sealed record ArppEntryDetails(
    long Id,
    int SortOrder,
    string? SerialNumber,
    string? PppNumber,
    string ProjectReference,
    int? ProjectId,
    string? ProjectName,
    string? ProjectCaseFileNumber,
    string? ProjectStatus,
    ArppCategory Category,
    decimal IpaCost,
    int? CfaOptionId,
    string Cfa,
    int? FundOptionId,
    string Fund,
    int? DfpdsScheduleId,
    string DfpdsSchedule,
    string RowVersion)
{
    public bool HasUnresolvedReferenceData => !CfaOptionId.HasValue || !FundOptionId.HasValue || !DfpdsScheduleId.HasValue;
}

public sealed record ArppCategorySummary(
    ArppCategory Category,
    int Count,
    decimal TotalIpaCost);

public sealed record ArppProjectHistory(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string ProjectStatus,
    IReadOnlyList<ArppProjectHistoryItem> Items);

public sealed record ArppProjectHistoryItem(
    long EntryId,
    long IssueId,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string IssueName,
    DateOnly IssueDate,
    string? SerialNumber,
    string? PppNumber,
    ArppCategory Category,
    decimal IpaCost,
    string Cfa,
    string Fund,
    string DfpdsSchedule,
    bool IsAuthoritative);

public sealed record ArppProjectLookupItem(
    int Id,
    string Name,
    string? CaseFileNumber,
    string StatusLabel);
