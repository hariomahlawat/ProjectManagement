using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Configuration;

// SECTION: Project document OCR options
public sealed class ProjectDocumentOcrOptions
{
    // SECTION: File system configuration
    [Required(AllowEmptyStrings = false)]
    public string WorkRoot { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string InputSubpath { get; set; } = "input";

    [Required(AllowEmptyStrings = false)]
    public string OutputSubpath { get; set; } = "output";

    [Required(AllowEmptyStrings = false)]
    public string LogsSubpath { get; set; } = "logs";

    // SECTION: Feature toggles
    public bool EnableWorker { get; set; } = true;
}
