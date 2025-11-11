namespace ProjectManagement.Configuration;

// SECTION: Project document OCR options
public sealed class ProjectDocumentOcrOptions
{
    public string WorkRoot { get; set; } = "App_Data/project-ocr";

    public bool EnableWorker { get; set; } = true;
}
