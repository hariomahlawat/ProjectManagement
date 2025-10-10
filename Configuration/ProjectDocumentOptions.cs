using System;
using System.Collections.Generic;

namespace ProjectManagement.Configuration
{
    public class ProjectDocumentOptions
    {
        private string _storageSubPath = "docs";

        public string ProjectsSubpath { get; set; } = "projects";

        public string PhotosSubpath { get; set; } = string.Empty;

        public string DocumentsSubpath
        {
            get => _storageSubPath;
            set => _storageSubPath = value ?? string.Empty;
        }

        public string StorageSubPath
        {
            get => _storageSubPath;
            set => _storageSubPath = value ?? string.Empty;
        }

        public string CommentsSubpath { get; set; } = "comments";

        public string VideosSubpath { get; set; } = "videos";

        public string TempSubPath { get; set; } = "temp";

        public int MaxSizeMb { get; set; } = 25;

        public HashSet<string> AllowedMimeTypes { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "application/pdf"
            };

        public bool EnableVirusScan { get; set; }
    }
}
