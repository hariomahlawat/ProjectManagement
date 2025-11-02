using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Data.DocRepo;

public class Tag
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Name { get; set; } = null!;

    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();
}
