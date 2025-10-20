namespace ProjectManagement.Areas.ProjectOfficeReports.Domain
{
    public class ProliferationGranular
    {
        public Guid Id { get; set; }
        public int ProjectId { get; set; }
        public ProliferationSource Source { get; set; } = ProliferationSource.Sdd;
        public string UnitName { get; set; } = default!;
        public DateOnly ProliferationDate { get; set; }
        public int Quantity { get; set; }
        public string? Remarks { get; set; }

        public ApprovalStatus ApprovalStatus { get; set; }
        public string SubmittedByUserId { get; set; } = default!;
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedOnUtc { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime LastUpdatedOnUtc { get; set; }
        public byte[] RowVersion { get; set; } = default!;
    }
}
