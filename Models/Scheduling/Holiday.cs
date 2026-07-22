namespace ProjectManagement.Models.Scheduling;

public class Holiday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public HolidayType Type { get; set; } = HolidayType.Gazetted;

    /// <summary>
    /// For Restricted Holidays, indicates that the office has formally declared the day
    /// as an office holiday. Gazetted Holidays are always observed and normalised to true.
    /// </summary>
    public bool IsObservedAsOfficeHoliday { get; set; } = true;

    public string? AuthorityReference { get; set; }
    public string? ObservanceRemarks { get; set; }
    public DateTime? ObservanceChangedUtc { get; set; }
    public string? ObservanceChangedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public bool AffectsWorkingCalendar =>
        Type == HolidayType.Gazetted || IsObservedAsOfficeHoliday;
}
