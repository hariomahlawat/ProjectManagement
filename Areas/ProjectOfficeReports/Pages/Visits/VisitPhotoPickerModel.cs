namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

// What _PhotoPicker.cshtml needs: the input id, the limits (enforced again on the server) and any server error.
public sealed record VisitPhotoPickerModel(
    string InputId,
    string HelpText,
    int MaxFiles,
    long MaxBytes,
    long MaxBatchBytes,
    string? Caption,
    string? Error);
