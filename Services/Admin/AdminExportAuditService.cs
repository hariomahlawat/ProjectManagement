using System.Globalization;

namespace ProjectManagement.Services.Admin;

/// <summary>One kind of personal-data export: its audit action and the plain title the audit log shows.</summary>
public sealed record AdminExportKind(string Action, string Title)
{
    public static readonly AdminExportKind Users = new("AdminExport.Users", "Users exported");
    public static readonly AdminExportKind AccessReview = new("AdminExport.AccessReview", "Access review exported");
    public static readonly AdminExportKind AuditLog = new("AdminExport.AuditLog", "Audit log exported");
    public static readonly AdminExportKind SignIns = new("AdminExport.SignIns", "Sign-in activity exported");
    public static readonly AdminExportKind ErpUsage = new("AdminExport.ErpUsage", "ERP usage exported");

    public static IReadOnlyList<AdminExportKind> All { get; } =
        new[] { Users, AccessReview, AuditLog, SignIns, ErpUsage };
}

/// <summary>What one export handed out: which export, the filters it used and how many rows.</summary>
public sealed record AdminExportRecord(
    AdminExportKind Kind,
    int RowCount,
    IReadOnlyDictionary<string, string?> Filters,
    bool Truncated = false);

public interface IAdminExportAuditService
{
    /// <summary>
    /// Writes one audit event for an export, by the signed-in user. Call it before returning the
    /// file: if the audit write fails the exception is not caught, so no file leaves unrecorded.
    /// </summary>
    Task RecordAsync(AdminExportRecord export, CancellationToken cancellationToken = default);
}

public sealed class AdminExportAuditService : IAdminExportAuditService
{
    public const string EntityType = "Export";

    private readonly IAdminAuditService _audit;

    public AdminExportAuditService(IAdminAuditService audit)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public Task RecordAsync(AdminExportRecord export, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(export.Kind);

        // Only the filters that were actually set, in a stable order, so the audit detail reads
        // as "what was asked for" rather than a list of blanks.
        var filters = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in export.Filters ?? new Dictionary<string, string?>())
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                filters[key.Trim()] = value.Trim();
            }
        }

        var rows = export.RowCount == 1
            ? "1 row"
            : $"{export.RowCount.ToString("N0", CultureInfo.InvariantCulture)} rows";
        var message = export.Truncated
            ? $"{export.Kind.Title}: {rows}, stopped at the export limit."
            : $"{export.Kind.Title}: {rows}.";

        return _audit.RecordAsync(
            new AdminAuditEntry(
                export.Kind.Action,
                EntityType,
                After: new
                {
                    Export = export.Kind.Title,
                    Rows = export.RowCount,
                    export.Truncated,
                    Filters = filters
                },
                Message: message),
            cancellationToken);
    }
}

/// <summary>The plain last row a cut-off export ends with.</summary>
public static class AdminExportNotice
{
    public static string Truncated(int rowCount) =>
        $"Export stopped at {rowCount.ToString("N0", CultureInfo.InvariantCulture)} rows; narrow the filters to get the rest.";

    public static IReadOnlyList<object?> TruncatedRow(int rowCount) => new object?[] { Truncated(rowCount) };
}
