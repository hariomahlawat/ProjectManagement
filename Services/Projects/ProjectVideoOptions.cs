using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Projects
{
    public class ProjectVideoOptions
    {
        public long MaxFileSizeBytes { get; set; } = 200 * 1024 * 1024;

        public HashSet<string> AllowedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "video/mp4",
            "video/webm",
            "video/ogg"
        };

        public TimeSpan? MaxDuration { get; set; }

        public string? StorageRootOverride { get; set; }

        public string PosterFileExtension { get; set; } = ".jpg";
    }
}
