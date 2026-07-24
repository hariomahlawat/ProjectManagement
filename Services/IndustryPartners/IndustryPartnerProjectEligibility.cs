using System.Collections.Generic;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.IndustryPartners;

// SECTION: Joint Development Partner linking eligibility rules
public static class IndustryPartnerProjectEligibility
{
    // JDP is a portfolio attribute for every live project. A project has one linked
    // organisation or remains unlinked to represent its nil position.
    public static bool IsEligibleForJdpLink(Project project, IEnumerable<ProjectStage> stages)
    {
        _ = stages;
        return !project.IsDeleted && !project.IsArchived;
    }
}
