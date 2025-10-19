namespace ProjectManagement.Areas.ProjectOfficeReports.Domain;

public static class ProliferationSourceExtensions
{
    public static string ToDisplayName(this ProliferationSource source) => source switch
    {
        ProliferationSource.Sdd => "SDD",
        ProliferationSource.Abw515 => "515 ABW",
        _ => source.ToString(),
    };
}
