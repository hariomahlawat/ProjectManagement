using System.Collections.Generic;

namespace ProjectManagement.Features.Process;

public static class StageChecklistCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Checklists =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FS"] = new[]
            {
                "Confirm business need and scope with key stakeholders",
                "Document available budgetary approvals and constraints",
                "Compile feasibility report with risk and impact analysis"
            },
            ["IPA"] = new[]
            {
                "Prepare in-principle approval note with executive summary",
                "Collect endorsements from finance, legal and technical teams",
                "Upload supporting documents to the procurement workspace"
            },
            ["SOW"] = new[]
            {
                "Draft detailed statement of work with deliverables and timelines",
                "Align scope with compliance, security and sustainability guidelines",
                "Validate acceptance criteria with the requesting department"
            },
            ["AON"] = new[]
            {
                "Create acceptance of necessity proposal for approval board",
                "Attach comparative market study and cost justification",
                "Capture board decisions and action items in the tracker"
            },
            ["BID"] = new[]
            {
                "Publish tender package to approved vendor list",
                "Schedule bidder conference and capture clarifications",
                "Monitor bid submission status and acknowledge receipts"
            },
            ["TEC"] = new[]
            {
                "Constitute evaluation committee and assign reviewers",
                "Distribute technical scorecards and evaluation criteria",
                "Consolidate evaluation results and prepare recommendation"
            },
            ["BM"] = new[]
            {
                "Identify benchmark sources relevant to the procurement category",
                "Validate price points against historical procurement data",
                "Summarise benchmarking insights for negotiation strategy"
            },
            ["COB"] = new[]
            {
                "Schedule commercial opening with finance and legal observers",
                "Verify bid security, compliance documents and pricing sheets",
                "Document minutes and communicate outcomes to stakeholders"
            },
            ["PNC"] = new[]
            {
                "Form negotiation team and define negotiation objectives",
                "Align negotiation levers with risk and value analysis",
                "Record negotiation proceedings and final agreed terms"
            },
            ["EAS"] = new[]
            {
                "Prepare expenditure sanction dossier with financial impacts",
                "Ensure approvals align with delegated financial authority matrix",
                "Archive sanction documents for audit readiness"
            },
            ["SO"] = new[]
            {
                "Draft supply order with clear deliverables and payment terms",
                "Validate supplier master data and compliance requirements",
                "Circulate signed order to vendor and internal teams"
            },
            ["DEVP"] = new[]
            {
                "Confirm project kickoff readiness with vendor and stakeholders",
                "Track development milestones against agreed plan",
                "Raise and resolve issues through change control process"
            },
            ["ATP"] = new[]
            {
                "Define acceptance scenarios and test environment setup",
                "Coordinate test execution with business and technical leads",
                "Sign-off acceptance certificates and log residual observations"
            },
            ["PAYMENT"] = new[]
            {
                "Receive vendor invoice and verify against contractual terms",
                "Complete three-way match with order and delivery documents",
                "Submit payment recommendation and track disbursement"
            }
        };

    public static IReadOnlyList<string> GetChecklist(string stageCode)
    {
        if (Checklists.TryGetValue(stageCode, out var items))
        {
            return items;
        }

        return Array.Empty<string>();
    }
}
