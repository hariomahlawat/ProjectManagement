namespace ProjectManagement.Configuration;

// SECTION: Project document OCR options
public sealed class ProjectDocumentOcrOptions
{
    // SECTION: File system configuration
    public string WorkRoot { get; set; } = string.Empty;

    public string InputSubpath { get; set; } = "input";

    public string OutputSubpath { get; set; } = "output";

    public string LogsSubpath { get; set; } = "logs";

    // SECTION: Feature toggles
    public bool EnableWorker { get; set; } = true;
}
