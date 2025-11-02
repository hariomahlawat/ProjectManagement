namespace ProjectManagement.Data.DocRepo;

public class DocumentTag
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
