using System;
using System.Collections.Generic;

namespace ProjectManagement.Configuration;

public sealed class IprAttachmentOptions
{
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public HashSet<string> AllowedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf"
    };
}
