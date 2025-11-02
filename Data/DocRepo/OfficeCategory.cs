using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

public class OfficeCategory
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 100;
}
