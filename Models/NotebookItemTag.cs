namespace ProjectManagement.Models;

// SECTION: My Notebook module types
public class NotebookItemTag
{
    public Guid NotebookItemId { get; set; }
    public NotebookItem? NotebookItem { get; set; }
    public int NotebookTagId { get; set; }
    public NotebookTag? NotebookTag { get; set; }
}
