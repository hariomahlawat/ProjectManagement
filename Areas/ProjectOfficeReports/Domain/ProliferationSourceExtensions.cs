namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public static class ProliferationSourceExtensions
{
    public static string ToDisplayName(this ProliferationSource source)
    {
        return source switch
        {
            ProliferationSource.Sdd => "SDD",
            ProliferationSource.Abw515 => "515 ABW",
            _ => "Unknown"
        };
    }
}
