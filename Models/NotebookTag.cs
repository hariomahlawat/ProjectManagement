using System.ComponentModel.DataAnnotations;

// SECTION: My Notebook module types
namespace ProjectManagement.Models;

public class NotebookTag
{
    public int Id { get; set; }
    [Required] public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }
    [Required, MaxLength(64)] public string Name { get; set; } = string.Empty;
    [MaxLength(64)] public string NormalizedName { get; set; } = string.Empty;
    public ICollection<NotebookItemTag> Items { get; set; } = new List<NotebookItemTag>();
}
