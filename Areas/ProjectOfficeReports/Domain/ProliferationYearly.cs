namespace ProjectManagement.Areas.ProjectOfficeReports.Domain
{
    public class ProliferationYearly
    {
        public Guid Id { get; set; }
        public int ProjectId { get; set; }
        public ProliferationSource Source { get; set; }
        public int Year { get; set; }
        public int TotalQuantity { get; set; }
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
