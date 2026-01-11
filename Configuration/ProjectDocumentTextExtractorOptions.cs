using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Configuration;

// SECTION: Project document text extraction options
public sealed class ProjectDocumentTextExtractorOptions
{
    // SECTION: Storage configuration
    [Required(AllowEmptyStrings = false)]
    public string DerivativeStoragePrefix { get; set; } = "ocr";

    // SECTION: LibreOffice conversion configuration
    public bool EnablePdfConversion { get; set; }

    public string? LibreOfficeExecutablePath { get; set; }
}
