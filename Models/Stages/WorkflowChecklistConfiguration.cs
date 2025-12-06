using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

/// <summary>
/// Holds version-aware checklist definitions sourced from workflow configuration.
/// </summary>
public static class WorkflowChecklistConfiguration
{
    // SECTION: Backing store
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SharedChecklist
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.FS] = new[]
            {
                "Confirm business need and scope with key stakeholders",
                "Document available budgetary approvals and constraints",
                "Compile feasibility report with risk and impact analysis"
            },
            [StageCodes.IPA] = new[]
            {
                "Prepare in-principle approval note with executive summary",
                "Collect endorsements from finance, legal and technical teams",
                "Upload supporting documents to the procurement workspace"
            },
            [StageCodes.SOW] = new[]
            {
                "Draft detailed statement of work with deliverables and timelines",
                "Align scope with compliance, security and sustainability guidelines",
                "Validate acceptance criteria with the requesting department"
            },
            [StageCodes.AON] = new[]
            {
                "Create acceptance of necessity proposal for approval board",
                "Attach comparative market study and cost justification",
                "Capture board decisions and action items in the tracker"
            },
            [StageCodes.BID] = new[]
            {
                "Publish tender package to approved vendor list",
                "Schedule bidder conference and capture clarifications",
                "Monitor bid submission status and acknowledge receipts"
            },
            [StageCodes.TEC] = new[]
            {
                "Constitute evaluation committee and assign reviewers",
                "Distribute technical scorecards and evaluation criteria",
                "Consolidate evaluation results and prepare recommendation"
            },
            [StageCodes.BM] = new[]
            {
                "Identify benchmark sources relevant to the procurement category",
                "Validate price points against historical procurement data",
                "Summarise benchmarking insights for negotiation strategy"
            },
            [StageCodes.COB] = new[]
            {
                "Schedule commercial opening with finance and legal observers",
                "Verify bid security, compliance documents and pricing sheets",
                "Document minutes and communicate outcomes to stakeholders"
            },
            [StageCodes.PNC] = new[]
            {
                "Form negotiation team and define negotiation objectives",
                "Align negotiation levers with risk and value analysis",
                "Record negotiation proceedings and final agreed terms"
            },
            [StageCodes.EAS] = new[]
            {
                "Prepare expenditure sanction dossier with financial impacts",
                "Ensure approvals align with delegated financial authority matrix",
                "Archive sanction documents for audit readiness"
            },
            [StageCodes.SO] = new[]
            {
                "Draft supply order with clear deliverables and payment terms",
                "Validate supplier master data and compliance requirements",
                "Circulate signed order to vendor and internal teams"
            },
            [StageCodes.DEVP] = new[]
            {
                "Confirm project kickoff readiness with vendor and stakeholders",
                "Track development milestones against agreed plan",
                "Raise and resolve issues through change control process"
            },
            [StageCodes.ATP] = new[]
            {
                "Define acceptance scenarios and test environment setup",
                "Coordinate test execution with business and technical leads",
                "Sign-off acceptance certificates and log residual observations"
            },
            [StageCodes.PAYMENT] = new[]
            {
                "Receive vendor invoice and verify against contractual terms",
                "Complete three-way match with order and delivery documents",
                "Submit payment recommendation and track disbursement"
            }
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> VersionedChecklists
        = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            [ProcurementWorkflow.VersionV1] = BuildVersionLookup(ProcurementWorkflow.VersionV1),
            [ProcurementWorkflow.VersionV2] = BuildVersionLookup(ProcurementWorkflow.VersionV2)
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultLookup
        = BuildVersionLookup(PlanConstants.DefaultStageTemplateVersion);

    // SECTION: API
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> All
        => VersionedChecklists;

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetForVersion(string? workflowVersion)
    {
        if (!string.IsNullOrWhiteSpace(workflowVersion)
            && VersionedChecklists.TryGetValue(workflowVersion, out var lookup))
        {
            return lookup;
        }

        return DefaultLookup;
    }

    // SECTION: Helpers
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildVersionLookup(string workflowVersion)
    {
        var stageCodes = ProcurementWorkflow.StageCodesFor(workflowVersion);
        var lookup = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var stageCode in stageCodes)
        {
            lookup[stageCode] = SharedChecklist.TryGetValue(stageCode, out var items)
                ? items
                : Array.Empty<string>();
        }

        return lookup;
    }
}
