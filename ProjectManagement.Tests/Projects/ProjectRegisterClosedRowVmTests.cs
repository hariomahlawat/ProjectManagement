using ProjectManagement.Models;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectRegisterClosedRowVmTests
{
    [Fact]
    public void Completed_UsesTheDayThenMonthThenYear()
    {
        Assert.Equal("4 Mar 2026", Row(p => { p.CompletedOn = new DateOnly(2026, 3, 4); p.CompletedYear = 2026; }).CompletedText);
        Assert.Equal("Nov 2025", Row(p => { p.CompletedYear = 2025; p.CompletedMonth = 11; }).CompletedText);
        Assert.Equal("2019", Row(p => p.CompletedYear = 2019).CompletedText);
        Assert.Equal("Not recorded", Row(_ => { }).CompletedText);
    }

    [Fact]
    public void Tot_FollowsTheRecordedStatusAndRepeatBuildsHaveNone()
    {
        Assert.Equal(("Completed", "ok"), Tot(p => p.Tot = new ProjectTot { Status = ProjectTotStatus.Completed }));
        Assert.Equal(("In progress", "accent"), Tot(p => p.Tot = new ProjectTot { Status = ProjectTotStatus.InProgress }));
        Assert.Equal(("Not started", "late"), Tot(p => p.Tot = new ProjectTot { Status = ProjectTotStatus.NotStarted }));
        Assert.Equal(("Not required", "none"), Tot(p => p.Tot = new ProjectTot { Status = ProjectTotStatus.NotRequired }));
        Assert.Equal(("Not recorded", "none"), Tot(_ => { }));
        Assert.Equal(("Not applicable", "none"), Tot(p => { p.IsBuild = true; p.Tot = new ProjectTot { Status = ProjectTotStatus.Completed }; }));
    }

    [Fact]
    public void Record_Cancellation_StatusAndBuild()
    {
        var legacy = Row(p => p.IsLegacy = true);
        Assert.Equal("Legacy entry", legacy.RecordText);
        Assert.True(legacy.IsLegacy);
        Assert.Equal("Recorded in PRISM", Row(_ => { }).RecordText);

        var cancelled = Row(p =>
        {
            p.LifecycleStatus = ProjectLifecycleStatus.Cancelled;
            p.CancelledOn = new DateOnly(2026, 2, 14);
            p.CancelReason = "  Requirement withdrawn.  ";
        });
        Assert.Equal("14 Feb 2026", cancelled.CancelledText);
        Assert.Equal("Requirement withdrawn.", cancelled.ReasonText);
        Assert.Equal(("Cancelled", "none"), (cancelled.StatusText, cancelled.StatusTone));
        Assert.Equal("—", Row(_ => { }).ReasonText);
        Assert.Equal("Not recorded", Row(_ => { }).CancelledText);

        Assert.Equal(("Active", "accent"), (Row(p => p.LifecycleStatus = ProjectLifecycleStatus.Active).StatusText, "accent"));
        Assert.Equal("Repeat build", Row(p => p.IsBuild = true).BuildText);
        Assert.Equal("New development", Row(_ => { }).BuildText);
    }

    private static (string, string) Tot(Action<Project> setup)
    {
        var row = Row(setup);
        return (row.TotText, row.TotTone);
    }

    private static ProjectRegisterClosedRowVm Row(Action<Project> setup)
    {
        var project = new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };
        setup(project);
        return ProjectRegisterClosedRowVm.Create(project);
    }
}
