using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public class ProjectType
{
    public int Id { get; set; }

    // SECTION: Core lookup fields
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // SECTION: Navigation
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
