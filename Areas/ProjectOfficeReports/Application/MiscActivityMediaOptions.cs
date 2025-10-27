namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class MiscActivityMediaOptions
{
    public string StoragePrefix { get; set; } = "project-office/misc-activities";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } =
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    };
}
