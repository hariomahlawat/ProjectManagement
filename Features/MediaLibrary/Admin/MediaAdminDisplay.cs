using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Admin;

public static class MediaAdminDisplay
{
    public static string AvailabilityStatusLabel(MediaAvailabilityStatus status) => status switch
    {
        MediaAvailabilityStatus.SourceMissing => "Source file missing",
        MediaAvailabilityStatus.AccessDenied => "Access denied",
        MediaAvailabilityStatus.TemporarilyUnavailable => "Temporarily unavailable",
        MediaAvailabilityStatus.Unsupported => "Unsupported media",
        MediaAvailabilityStatus.Corrupt => "File appears corrupt",
        _ => status.ToString()
    };

    public static string SummarizeUnavailableReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "The source file could not be opened.";
        }

        var normalized = reason.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Contains("No project-photo derivative", StringComparison.OrdinalIgnoreCase))
            return "The project photo file is no longer available.";
        if (normalized.Contains("No visit-photo asset", StringComparison.OrdinalIgnoreCase))
            return "The visit photo file is no longer available.";
        if (normalized.Contains("No social-media photo asset", StringComparison.OrdinalIgnoreCase))
            return "The event photo file is no longer available.";
        if (normalized.Contains("access", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("denied", StringComparison.OrdinalIgnoreCase))
            return "PRISM does not currently have permission to read the file.";

        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }
}
