namespace ProjectManagement.Services.Arpp;

public enum ArppReferenceDataKind
{
    Cfa = 1,
    Fund = 2,
    DfpdsSchedule = 3
}

public sealed record ArppReferenceOption(
    int Id,
    string Value,
    string? Description,
    bool IsActive,
    int SortOrder,
    int UsageCount,
    string RowVersion)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Description)
        ? Value
        : $"{Value} — {Description}";
}

public sealed record ArppReferenceDataSet(
    IReadOnlyList<ArppReferenceOption> CfaOptions,
    IReadOnlyList<ArppReferenceOption> FundOptions,
    IReadOnlyList<ArppReferenceOption> DfpdsSchedules);

public sealed record ArppReferenceDataAdminSnapshot(
    IReadOnlyList<ArppReferenceOption> CfaOptions,
    IReadOnlyList<ArppReferenceOption> FundOptions,
    IReadOnlyList<ArppReferenceOption> DfpdsSchedules)
{
    public int Total => CfaOptions.Count + FundOptions.Count + DfpdsSchedules.Count;
    public int Active => CfaOptions.Count(item => item.IsActive)
        + FundOptions.Count(item => item.IsActive)
        + DfpdsSchedules.Count(item => item.IsActive);
    public int Inactive => Total - Active;
    public int InUse => CfaOptions.Count(item => item.UsageCount > 0)
        + FundOptions.Count(item => item.UsageCount > 0)
        + DfpdsSchedules.Count(item => item.UsageCount > 0);
}

public sealed record ArppReferenceDataSaveCommand(
    ArppReferenceDataKind Kind,
    int? Id,
    string Value,
    string? Description,
    int SortOrder,
    string? RowVersion,
    string UserId,
    string? UserName);

public sealed record ArppReferenceDataActivationCommand(
    ArppReferenceDataKind Kind,
    int Id,
    bool IsActive,
    string RowVersion,
    string UserId,
    string? UserName);

public sealed record ArppReferenceDataCommandResult(
    bool Success,
    string Message,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors)
{
    public static ArppReferenceDataCommandResult Succeeded(string message)
        => new(true, message, new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    public static ArppReferenceDataCommandResult Failed(
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fieldErrors = null)
        => new(false, message, fieldErrors ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
}

public interface IArppReferenceDataService
{
    Task<ArppReferenceDataSet> GetWorkspaceOptionsAsync(
        IReadOnlyCollection<int> selectedCfaIds,
        IReadOnlyCollection<int> selectedFundIds,
        IReadOnlyCollection<int> selectedDfpdsIds,
        CancellationToken cancellationToken = default);

    Task<ArppReferenceDataAdminSnapshot> GetAdminSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<ArppReferenceDataCommandResult> SaveAsync(
        ArppReferenceDataSaveCommand command,
        CancellationToken cancellationToken = default);

    Task<ArppReferenceDataCommandResult> SetActiveAsync(
        ArppReferenceDataActivationCommand command,
        CancellationToken cancellationToken = default);
}
