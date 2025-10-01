using System;

namespace ProjectManagement.Services.Documents;

public sealed record DocumentFileDescriptor(
    string StorageKey,
    string OriginalFileName,
    long Length,
    string ContentType)
{
    public string StorageKey { get; init; } = StorageKey ?? throw new ArgumentNullException(nameof(StorageKey));

    public string OriginalFileName { get; init; } =
        OriginalFileName ?? throw new ArgumentNullException(nameof(OriginalFileName));

    public string ContentType { get; init; } =
        ContentType ?? throw new ArgumentNullException(nameof(ContentType));
}
