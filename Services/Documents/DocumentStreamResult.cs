using System;
using System.IO;

namespace ProjectManagement.Services.Documents;

public sealed record DocumentStreamResult(
    Stream Stream,
    string FileName,
    string ContentType,
    long Length,
    int FileStamp)
{
    public Stream Stream { get; init; } = Stream ?? throw new ArgumentNullException(nameof(Stream));
    public string FileName { get; init; } = FileName ?? throw new ArgumentNullException(nameof(FileName));
    public string ContentType { get; init; } = ContentType ?? throw new ArgumentNullException(nameof(ContentType));
}
