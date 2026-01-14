namespace ProjectManagement.Services.IndustryPartners
{
public sealed record PartnerOverviewUpdateResult(bool Updated, bool NotFound, bool Conflict)
{
    // Section: Named results
    public static PartnerOverviewUpdateResult Success => new(true, false, false);
    public static PartnerOverviewUpdateResult Missing => new(false, true, false);
    public static PartnerOverviewUpdateResult ConcurrencyConflict => new(false, false, true);
}
}
