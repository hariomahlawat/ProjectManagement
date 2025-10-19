namespace ProjectManagement.Areas.ProjectOfficeReports.Domain
{
    public sealed class VwProliferationGranularYearly
    {
        public int ProjectId { get; set; }
        public ProliferationSource Source { get; set; }
        public int Year { get; set; }
        public int TotalQuantity { get; set; }
    }
}
