using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Projects
{
    public class ProjectPhotoOptions
    {
        public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

        public int MinWidth { get; set; } = 720;

        public int MinHeight { get; set; } = 540;

        public HashSet<string> AllowedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        public Dictionary<string, ProjectPhotoDerivativeOptions> Derivatives { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["xl"] = new ProjectPhotoDerivativeOptions { Width = 1600, Height = 1200, Quality = 90 },
            ["md"] = new ProjectPhotoDerivativeOptions { Width = 1200, Height = 900, Quality = 85 },
            ["sm"] = new ProjectPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 },
            ["xs"] = new ProjectPhotoDerivativeOptions { Width = 400, Height = 300, Quality = 75 }
        };

        public int MaxProcessingConcurrency { get; set; } = Math.Max(Environment.ProcessorCount / 2, 1);

        public int MaxEncodingConcurrency { get; set; } = Math.Max(Environment.ProcessorCount / 2, 1);

        public string StorageRoot { get; set; } = string.Empty;
    }

    public class ProjectPhotoDerivativeOptions
    {
        public int Width { get; set; }

        public int Height { get; set; }

        public int Quality { get; set; } = 85;
    }
}
