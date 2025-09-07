namespace ProjectManagement.Areas.Admin.Models
{
    /// <summary>
    /// Represents a card displayed on the admin dashboard.
    /// </summary>
    public record AdminCard(string Title, string Page, string Description, string CssClass);
}
