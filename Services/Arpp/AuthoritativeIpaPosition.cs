using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

public enum IpaPositionSource
{
    LegacyProjectFact = 1,
    Arpp = 2
}

public sealed record AuthoritativeIpaPosition(
    int ProjectId,
    decimal AmountInRupees,
    IpaPositionSource Source,
    ArppCategory? Category,
    int? FinancialYearStart,
    long? IssueId,
    string? IssueName,
    DateOnly? IssueDate,
    int? IssueSequence,
    long? EntryId,
    string? SerialNumber,
    string? PppNumber)
{
    public bool IsManagedByArpp => Source == IpaPositionSource.Arpp;

    public bool IsLegacyFallback => Source == IpaPositionSource.LegacyProjectFact;

    public bool IsDelisted => Category == ArppCategory.Delisted;

    public bool HasMeaningfulAmount => AmountInRupees > 0m;
}
