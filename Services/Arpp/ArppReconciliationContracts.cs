namespace ProjectManagement.Services.Arpp;

public sealed record ArppReconciliationResult(
    IReadOnlyList<ArppReconciliationItem> Items,
    IReadOnlyList<int> AvailableFinancialYears,
    int TotalUnlinkedEntries);

public sealed record ArppReconciliationItem(
    long EntryId,
    string EntryRowVersion,
    long IssueId,
    int FinancialYearStart,
    string IssueName,
    int IssueSequence,
    DateOnly IssueDate,
    string SerialNumber,
    string ProjectReference,
    decimal IpaCost,
    string CategoryLabel,
    bool IssueIsVerified,
    IReadOnlyList<ArppProjectSuggestion> Suggestions);

public sealed record ArppProjectSuggestion(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string StatusLabel,
    decimal? LegacyIpaCost,
    int ConfidencePercent);

public sealed record ArppReconciliationLinkInput(
    long EntryId,
    string EntryRowVersion,
    int ProjectId);

public sealed record ArppReconciliationCommand(
    IReadOnlyList<ArppReconciliationLinkInput> Links,
    string UserId,
    string? UserName);
