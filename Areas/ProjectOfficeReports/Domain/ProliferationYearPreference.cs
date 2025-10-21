namespace ProjectManagement.Areas.ProjectOfficeReports.Domain
{
    public class ProliferationYearPreference
    {
        public Guid Id { get; set; }
        public int ProjectId { get; set; }
        public ProliferationSource Source { get; set; }
        public int Year { get; set; }
        public YearPreferenceMode Mode { get; set; } = YearPreferenceMode.UseYearlyAndGranular;

        public string SetByUserId { get; set; } = default!;
        public DateTime SetOnUtc { get; set; }
    }
}
