using System.Globalization;
using ProjectManagement.Models.Arpp;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

// SECTION: Procurement editor model
// One place decides which procurement facts are editable, so the Project Overview panel and
// the Project Officer briefing desk show the same form with the same locks. canRecordFact
// comes from ProcurementFactAvailabilityService (ProcurementStageRules.CanRecordFact).
public static class ProcurementEditVmFactory
{
    public static ProcurementEditVm Create(
        int projectId,
        ProcurementAtAGlanceVm procurement,
        Func<string, bool> canRecordFact,
        string? returnUrl = null,
        string idPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(procurement);
        ArgumentNullException.ThrowIfNull(canRecordFact);

        return new ProcurementEditVm
        {
            Input = new ProcurementEditInput
            {
                ProjectId = projectId,
                IpaCost = procurement.IpaCost,
                AonCost = procurement.AonCost,
                BenchmarkCost = procurement.BenchmarkCost,
                L1Cost = procurement.L1Cost,
                PncCost = procurement.PncCost,
                SupplyOrderDate = procurement.SupplyOrderDate
            },
            CanEditIpaCost =
                canRecordFact(ProcurementStageRules.StageForIpaCost) &&
                !procurement.IsIpaManagedByArpp,
            IsIpaManagedByArpp = procurement.IsIpaManagedByArpp,
            IpaManagementMessage = BuildIpaManagementMessage(procurement),
            CanEditAonCost = canRecordFact(ProcurementStageRules.StageForAonCost),
            CanEditBenchmarkCost = canRecordFact(ProcurementStageRules.StageForBenchmarkCost),
            CanEditL1Cost = canRecordFact(ProcurementStageRules.StageForL1Cost),
            CanEditPncCost = canRecordFact(ProcurementStageRules.StageForPncCost),
            CanEditSupplyOrderDate = canRecordFact(ProcurementStageRules.StageForSupplyOrder),
            ReturnUrl = returnUrl,
            IdPrefix = idPrefix
        };
    }

    public static string? BuildIpaManagementMessage(ProcurementAtAGlanceVm procurement)
    {
        var position = procurement.IpaPosition;
        if (position?.IsManagedByArpp != true)
        {
            return null;
        }

        var issueSequence = position.IssueSequence.GetValueOrDefault();
        var documentLabel = issueSequence == 0
            ? "Original ARPP"
            : $"Addendum No. {issueSequence}";

        var parts = new List<string>
        {
            "Managed through ARPP",
            documentLabel
        };

        if (position.IssueDate.HasValue)
        {
            parts.Add(position.IssueDate.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture));
        }

        if (!string.IsNullOrWhiteSpace(position.SerialNumber))
        {
            parts.Add($"Ser No. {position.SerialNumber.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(position.PppNumber))
        {
            parts.Add($"PPP No. {position.PppNumber.Trim()}");
        }

        if (position.Category.HasValue)
        {
            parts.Add(ArppDisplayNames.For(position.Category.Value));
        }

        return string.Join(" · ", parts) + ". Update the authoritative value in the ARPP register.";
    }
}
