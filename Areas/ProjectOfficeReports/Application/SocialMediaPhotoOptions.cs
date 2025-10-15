using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class SocialMediaPhotoOptions
{
    private int? _minWidth;
    private int? _minHeight;

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public int? MinWidth
    {
        get => _minWidth;
        set => _minWidth = NormalizeMinimum(value);
    }

    public int? MinHeight
    {
        get => _minHeight;
        set => _minHeight = NormalizeMinimum(value);
    }

    public HashSet<string> AllowedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public Dictionary<string, SocialMediaPhotoDerivativeOptions> Derivatives { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["story"] = new SocialMediaPhotoDerivativeOptions { Width = 1080, Height = 1920, Quality = 85 },
        ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
        ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 }
    };

    public string StoragePrefix { get; set; } = "org/social/{eventId}";

    private static int? NormalizeMinimum(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return value > 0 ? value : null;
    }
}

public sealed class SocialMediaPhotoDerivativeOptions
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int Quality { get; set; } = 85;
}
