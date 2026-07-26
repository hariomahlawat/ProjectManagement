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
    string SerialNumber,
    string ProjectReference,
    int? ProjectId,
    ArppCategory? Category,
    decimal? IpaCost,
    string Cfa,
    string Fund,
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
    decimal TotalIpaCost,
    int LinkedEntries,
    int UnlinkedEntries);

public sealed record ArppFinancialYearGroup(
    int FinancialYearStart,
    IReadOnlyList<ArppIssueListItem> Issues,
    int EntryCount,
    decimal TotalIpaCost);

public sealed record ArppIssueListItem(
    long Id,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    int EntryCount,
    decimal TotalIpaCost,
    int NewCount,
    int CommittedLiabilityCount,
    int CarryForwardCount,
    int DelistedCount,
    int LinkedCount,
    int UnlinkedCount,
    DateTimeOffset UpdatedAtUtc,
    bool HasAttachment);

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
    ArppAttachmentDetails? Attachment);

public sealed record ArppEntryDetails(
    long Id,
    int SortOrder,
    string SerialNumber,
    string ProjectReference,
    int? ProjectId,
    string? ProjectName,
    string? ProjectCaseFileNumber,
    string? ProjectStatus,
    ArppCategory Category,
    decimal IpaCost,
    string Cfa,
    string Fund,
    string DfpdsSchedule,
    string RowVersion);

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
    string SerialNumber,
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
