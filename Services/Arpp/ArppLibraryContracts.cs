using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

public sealed record ArppLibraryNavigation(
    IReadOnlyList<ArppLibraryFinancialYear> FinancialYears,
    int PublishedDocumentCount);

public sealed record ArppLibraryFinancialYear(
    int FinancialYearStart,
    IReadOnlyList<ArppLibraryDocumentLink> Documents);

public sealed record ArppLibraryDocumentLink(
    long IssueId,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    int RowCount,
    decimal ApprovedRowValue,
    decimal DelistedRowValue);

public sealed record ArppLibraryDocument(
    long IssueId,
    int RevisionNumber,
    int FinancialYearStart,
    ArppIssueKind Kind,
    int IssueSequence,
    string Name,
    DateOnly IssueDate,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<ArppLibraryRow> Rows,
    ArppLibraryAttachment Attachment)
{
    public decimal ApprovedRowValue => Rows
        .Where(row => row.Category is ArppCategory.New or ArppCategory.CommittedLiability or ArppCategory.CarryForward)
        .Sum(row => row.IpaCost);

    public decimal DelistedRowValue => Rows
        .Where(row => row.Category == ArppCategory.Delisted)
        .Sum(row => row.IpaCost);
}

public sealed record ArppLibraryCurrentPosition(
    int FinancialYearStart,
    IReadOnlyList<ArppLibraryCurrentRow> ApprovedRows,
    IReadOnlyList<ArppLibraryCurrentRow> DelistedRows,
    IReadOnlyList<ArppLibraryUnlinkedRow> UnlinkedRows,
    int TotalUnlinkedDocumentRows)
{
    public decimal ApprovedIpaValue => ApprovedRows.Sum(row => row.IpaCost);
    public decimal DelistedIpaValue => DelistedRows.Sum(row => row.IpaCost);

    // Kept as an explicit semantic alias for callers written against the earlier contract.
    public int UnlinkedDocumentRows => TotalUnlinkedDocumentRows;
}

public sealed record ArppLibraryRow(
    long EntryId,
    int SortOrder,
    string? SerialNumber,
    string? PppNumber,
    string ProjectReference,
    int? ProjectId,
    string? ProjectName,
    string? ProjectStatus,
    ArppCategory Category,
    decimal IpaCost,
    string Cfa,
    string Fund,
    string DfpdsSchedule);

public sealed record ArppLibraryCurrentRow(
    long EntryId,
    int ProjectId,
    string ProjectReference,
    string ProjectName,
    string ProjectStatus,
    string? SerialNumber,
    string? PppNumber,
    ArppCategory Category,
    decimal IpaCost,
    string Cfa,
    string Fund,
    string DfpdsSchedule,
    int SourceFinancialYearStart,
    long SourceIssueId,
    string SourceIssueName,
    ArppIssueKind SourceKind,
    int SourceSequence,
    DateOnly SourceIssueDate);

public sealed record ArppLibraryUnlinkedRow(
    long EntryId,
    string ProjectReference,
    string? SerialNumber,
    string? PppNumber,
    ArppCategory Category,
    decimal IpaCost,
    string Cfa,
    string Fund,
    string DfpdsSchedule,
    int SourceFinancialYearStart,
    long SourceIssueId,
    string SourceIssueName,
    ArppIssueKind SourceKind,
    int SourceSequence,
    DateOnly SourceIssueDate);

public sealed record ArppLibraryProjectHistory(
    int ProjectId,
    string ProjectName,
    string? CaseFileNumber,
    string ProjectStatus,
    IReadOnlyList<ArppLibraryProjectHistoryRow> Rows);

public sealed record ArppLibraryProjectHistoryRow(
    long EntryId,
    int FinancialYearStart,
    long SourceIssueId,
    string SourceIssueName,
    ArppIssueKind SourceKind,
    int SourceSequence,
    DateOnly SourceIssueDate,
    string? SerialNumber,
    string? PppNumber,
    string ProjectReference,
    ArppCategory Category,
    decimal IpaCost,
    string Cfa,
    string Fund,
    string DfpdsSchedule);

public sealed record ArppLibraryAttachment(
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

public sealed record ArppLibraryAttachmentDownload(
    Stream Content,
    string ContentType,
    string DownloadFileName);
