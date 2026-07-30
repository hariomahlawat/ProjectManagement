using System;
using System.Collections.Generic;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Models.Stages;

/// <summary>
/// Holds version-aware stage guidance defaults sourced from workflow configuration.
/// The database remains authoritative after a stage guidance record is created.
/// </summary>
public static class WorkflowChecklistConfiguration
{
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
            },
            [StageCodes.TOT] = new[]
            {
                "Confirm the approved technology-transfer scope and deliverables",
                "Receive and verify technical documents, source material and training",
                "Record acceptance and close outstanding transfer actions"
            }
        };

    private static readonly IReadOnlyDictionary<string, string> SharedPurposes
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.FS] = "Establish the operational need, feasibility, broad scope, stakeholders and indicative resources.",
            [StageCodes.SOW] = "Define and vet the technical scope, deliverables, standards, acceptance criteria and responsibilities.",
            [StageCodes.IPA] = "Obtain in-principle approval to progress the proposal for detailed processing and costing.",
            [StageCodes.AON] = "Secure formal acceptance of necessity or sanction for procurement and associated expenditure.",
            [StageCodes.BID] = "Publish the approved tender package and manage bidder communication, clarifications and submissions.",
            [StageCodes.TEC] = "Evaluate technical compliance, capability, demonstrations and mandatory documentation.",
            [StageCodes.BM] = "Establish an independent and defensible benchmark for assessing price reasonableness.",
            [StageCodes.COB] = "Open the commercial bids of technically qualified firms and establish the commercial position.",
            [StageCodes.PNC] = "Conduct price negotiations where authorised and record the basis for the negotiated outcome.",
            [StageCodes.EAS] = "Obtain expenditure approval or financial sanction based on the evaluated commercial proposal.",
            [StageCodes.SO] = "Issue the supply order or contract with approved terms, milestones and obligations.",
            [StageCodes.DEVP] = "Execute development, integration, reviews and milestone monitoring against the contracted scope.",
            [StageCodes.ATP] = "Verify the delivered system against approved acceptance test procedures and contractual criteria.",
            [StageCodes.PAYMENT] = "Process payment against accepted deliverables, contractual milestones and supporting documents.",
            [StageCodes.TOT] = "Complete the approved transfer of technology, knowledge, documentation and sustainment arrangements."
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> VersionedChecklists
        = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            [ProcurementWorkflow.VersionV1] = BuildChecklistLookup(ProcurementWorkflow.VersionV1),
            [ProcurementWorkflow.VersionV2] = BuildChecklistLookup(ProcurementWorkflow.VersionV2)
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> VersionedPurposes
        = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ProcurementWorkflow.VersionV1] = BuildPurposeLookup(ProcurementWorkflow.VersionV1),
            [ProcurementWorkflow.VersionV2] = BuildPurposeLookup(ProcurementWorkflow.VersionV2)
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultChecklistLookup
        = BuildChecklistLookup(PlanConstants.DefaultStageTemplateVersion);

    private static readonly IReadOnlyDictionary<string, string> DefaultPurposeLookup
        = BuildPurposeLookup(PlanConstants.DefaultStageTemplateVersion);

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> All
        => VersionedChecklists;

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllPurposes
        => VersionedPurposes;

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetForVersion(string? workflowVersion)
    {
        if (!string.IsNullOrWhiteSpace(workflowVersion)
            && VersionedChecklists.TryGetValue(workflowVersion, out var lookup))
        {
            return lookup;
        }

        return DefaultChecklistLookup;
    }

    public static IReadOnlyDictionary<string, string> GetPurposesForVersion(string? workflowVersion)
    {
        if (!string.IsNullOrWhiteSpace(workflowVersion)
            && VersionedPurposes.TryGetValue(workflowVersion, out var lookup))
        {
            return lookup;
        }

        return DefaultPurposeLookup;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildChecklistLookup(string workflowVersion)
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

    private static IReadOnlyDictionary<string, string> BuildPurposeLookup(string workflowVersion)
    {
        var stageCodes = ProcurementWorkflow.StageCodesFor(workflowVersion);
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stageCode in stageCodes)
        {
            lookup[stageCode] = SharedPurposes.TryGetValue(stageCode, out var purpose)
                ? purpose
                : string.Empty;
        }

        return lookup;
    }
}
