using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProcurementEditVmFactoryTests
{
    [Fact]
    public void Create_UnlocksOnlyFieldsWhoseStageIsCompleted()
    {
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StageCodes.AON, StageCodes.COB };

        var vm = ProcurementEditVmFactory.Create(7, new ProcurementAtAGlanceVm(null, 1_00_000m, null, 2_00_000m, null, null), completed.Contains);

        Assert.Equal(7, vm.Input.ProjectId);
        Assert.Equal(1_00_000m, vm.Input.AonCost);
        Assert.True(vm.CanEditAonCost);
        Assert.True(vm.CanEditL1Cost);
        Assert.False(vm.CanEditBenchmarkCost);
        Assert.False(vm.CanEditSupplyOrderDate);
        Assert.Null(vm.ReturnUrl);
        Assert.Equal(string.Empty, vm.IdPrefix);
    }

    [Fact]
    public void Create_LocksIpaWhenArppManagesIt_AndExplainsWhere()
    {
        var procurement = new ProcurementAtAGlanceVm(5_00_000m, null, null, null, null, null)
        {
            IpaPosition = new AuthoritativeIpaPosition(7, 5_00_000m, IpaPositionSource.Arpp, null, 2026, 1, "ARPP 2026-27", new DateOnly(2026, 4, 1), 0, 9, "12", null)
        };

        var vm = ProcurementEditVmFactory.Create(7, procurement, _ => true);

        Assert.False(vm.CanEditIpaCost);
        Assert.True(vm.IsIpaManagedByArpp);
        Assert.StartsWith("Managed through ARPP · Original ARPP", vm.IpaManagementMessage);
        Assert.Contains("Ser No. 12", vm.IpaManagementMessage);
    }

    [Fact]
    public void Create_CarriesTheDeskReturnAddressAndIdPrefix()
    {
        var vm = ProcurementEditVmFactory.Create(7, ProcurementAtAGlanceVm.Empty, _ => false, "/Workspace?mode=project-officer", "desk-project-7-");

        Assert.Equal("/Workspace?mode=project-officer", vm.ReturnUrl);
        Assert.Equal("desk-project-7-", vm.IdPrefix);
    }
}
