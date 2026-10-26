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

        // SECTION: Amendment link
        // A Project Office amendment to an approved record is stored as its own pending
        // row pointing at that record, so the approved figure keeps counting until the
        // amendment is approved and is untouched when the amendment is rejected.
        public Guid? AmendsId { get; set; }

        public ApprovalStatus ApprovalStatus { get; set; }
        public string SubmittedByUserId { get; set; } = default!;
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedOnUtc { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime LastUpdatedOnUtc { get; set; }
        public byte[] RowVersion { get; set; } = default!;
    }
}
