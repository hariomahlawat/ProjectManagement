namespace ProjectManagement.Services.Arpp;

public sealed record ArppStoredAttachment(
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

public sealed record ArppAttachmentDetails(
    long Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string UploadedByUserId,
    DateTimeOffset UploadedAtUtc,
    string RowVersion);

public sealed record ArppAttachmentCommandResult(
    bool Success,
    string Message,
    string? Warning = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? FieldErrors = null)
{
    public static ArppAttachmentCommandResult Succeeded(string message, string? warning = null)
        => new(true, message, warning);

    public static ArppAttachmentCommandResult Failed(
        string message,
        string? fieldName = null,
        string? fieldError = null)
        => new(
            false,
            message,
            null,
            string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(fieldError)
                ? null
                : new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [fieldName] = new[] { fieldError }
                });
}

public sealed record ArppAttachmentDownload(
    Stream Content,
    string ContentType,
    string DownloadFileName,
    long SizeBytes);

public sealed record ArppExportFile(
    byte[] Content,
    string ContentType,
    string FileName);
