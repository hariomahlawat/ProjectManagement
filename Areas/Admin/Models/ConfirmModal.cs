namespace ProjectManagement.Areas.Admin.Models
{
    public record ConfirmModal(
        string Id,
        string Title,
        string Body,
        string FormId,
        string ConfirmText,
        string? FormAction = null,
        string? FormMethod = null);
}
