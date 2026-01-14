using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public static class IndustryPartnerTypeOptions
    {
        // Section: Type options
        public static readonly IReadOnlyList<IndustryPartnerTypeOption> Options = new List<IndustryPartnerTypeOption>
        {
            new("DPSU", "DPSU"),
            new("Private", "Private"),
            new("Startup", "Startup"),
            new("Academic", "Academic"),
            new("Foreign OEM", "Foreign OEM"),
            new("Other", "Other")
        };
    }

    public sealed record IndustryPartnerTypeOption(string Value, string Label);
}
