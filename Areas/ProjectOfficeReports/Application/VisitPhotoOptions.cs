using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class VisitPhotoOptions
{
    // SECTION: Upload limits
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxFilesPerUpload { get; set; } = 20;

    // SECTION: Validation thresholds
    public int MinWidth { get; set; } = 720;

    public int MinHeight { get; set; } = 540;

    // SECTION: Supported content types
    public HashSet<string> AllowedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    // SECTION: Derivative generation
    public Dictionary<string, VisitPhotoDerivativeOptions> Derivatives { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xl"] = new VisitPhotoDerivativeOptions { Width = 1600, Height = 1200, Quality = 90 },
        ["md"] = new VisitPhotoDerivativeOptions { Width = 1200, Height = 900, Quality = 85 },
        ["sm"] = new VisitPhotoDerivativeOptions { Width = 800, Height = 600, Quality = 80 },
        ["xs"] = new VisitPhotoDerivativeOptions { Width = 400, Height = 300, Quality = 75 }
    };

    // SECTION: Storage configuration
    public string StoragePrefix { get; set; } = "project-office-reports/visits";
}

public sealed class VisitPhotoDerivativeOptions
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int Quality { get; set; } = 85;
}
