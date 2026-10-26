using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Analytics;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>CoE (AR/VR) mode: proliferation and training of every AR/VR project, counted as the two trackers count them.</summary>
public sealed class ArVrImpactAnalyticsServiceTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2026, 10, 26); // calendar year 2026, training year 2026-27

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly ArVrImpactAnalyticsService _service;
    private readonly TechnicalCategory _arVr = new() { Name = "AR/ VR", CreatedByUserId = "seed" };
    private readonly TechnicalCategory _firing = new() { Name = "Firing Smls", CreatedByUserId = "seed" };

    public ArVrImpactAnalyticsServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.TechnicalCategories.AddRange(_arVr, _firing);
        _db.SaveChanges();
        _service = new ArVrImpactAnalyticsService(_db, new ProliferationAggregateReadService(_db));
    }

    private Project AddProject(string name, TechnicalCategory tech)
    {
        var project = new Project { Name = name, CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Completed, TechnicalCategoryId = tech.Id };
        _db.Projects.Add(project);
        _db.SaveChanges();
        return project;
    }

    private void AddYearly(Project project, int year, int quantity, ProliferationSource source = ProliferationSource.Sdd, ApprovalStatus status = ApprovalStatus.Approved) =>
        _db.ProliferationYearlies.Add(new ProliferationYearly { Id = Guid.NewGuid(), ProjectId = project.Id, Source = source, Year = year, TotalQuantity = quantity, ApprovalStatus = status, SubmittedByUserId = "seed", CreatedOnUtc = DateTime.UtcNow, LastUpdatedOnUtc = DateTime.UtcNow, RowVersion = new byte[] { 1 } });

    private void AddGranular(Project project, DateOnly date, string unit, int quantity, ApprovalStatus status = ApprovalStatus.Approved) =>
        _db.ProliferationGranularEntries.Add(new ProliferationGranular { Id = Guid.NewGuid(), ProjectId = project.Id, Source = ProliferationSource.Sdd, UnitName = unit, ProliferationDate = date, Quantity = quantity, ApprovalStatus = status, SubmittedByUserId = "seed", CreatedOnUtc = DateTime.UtcNow, LastUpdatedOnUtc = DateTime.UtcNow, RowVersion = new byte[] { 1 } });

    private Training AddTraining(DateOnly? start, int officers, int jcos, int ors, params Project[] projects)
    {
        var type = _db.TrainingTypes.FirstOrDefault() ?? new TrainingType { Id = Guid.NewGuid(), Name = "Simulator", CreatedByUserId = "seed", CreatedAtUtc = DateTimeOffset.UtcNow };
        if (_db.Entry(type).State == EntityState.Detached) { _db.TrainingTypes.Add(type); }
        var training = new Training { Id = Guid.NewGuid(), TrainingTypeId = type.Id, StartDate = start, CreatedByUserId = "seed", CreatedAtUtc = DateTimeOffset.UtcNow };
        training.Counters = new TrainingCounters { TrainingId = training.Id, Officers = officers, JuniorCommissionedOfficers = jcos, OtherRanks = ors, Total = officers + jcos + ors, Source = TrainingCounterSource.Roster, UpdatedAtUtc = DateTimeOffset.UtcNow };
        foreach (var project in projects)
        {
            training.ProjectLinks.Add(new TrainingProject { TrainingId = training.Id, ProjectId = project.Id });
        }
        _db.Trainings.Add(training);
        _db.SaveChanges();
        return training;
    }

    [Fact]
    public async Task Proliferation_CountsApprovedArVrRows_ByTheTrackersRule_WithReceivingUnits()
    {
        var vrRl = AddProject("VR RL", _arVr);
        var vrCmc = AddProject("VR CMC", _arVr);
        var firing = AddProject("Firing range", _firing);
        AddYearly(vrRl, 2022, 100);                                        // yearly only: 100
        AddYearly(vrRl, 2023, 10);                                         // yearly + granular, SDD default rule adds them: 10 + 5 + 5
        AddGranular(vrRl, new DateOnly(2023, 5, 1), "56 Inf Bde", 5);
        AddGranular(vrRl, new DateOnly(2023, 9, 1), "12 Armd Regt", 5);
        AddGranular(vrCmc, new DateOnly(2023, 2, 1), "56 Inf Bde", 3);     // the same unit again: one receiving unit
        AddYearly(vrCmc, 2024, 40, ProliferationSource.Abw515);           // ABW 515 series
        AddGranular(vrCmc, new DateOnly(2026, 1, 1), "AMC Centre", 2);     // this year
        AddGranular(vrRl, new DateOnly(2025, 1, 1), "Pending unit", 9, ApprovalStatus.Pending); // not approved: ignored
        AddYearly(firing, 2023, 500);                                      // not AR/VR: ignored
        AddGranular(vrRl, new DateOnly(25, 3, 1), "Typo unit", 7);         // a mistyped year the tracker's policy rejects: ignored
        await _db.SaveChangesAsync();

        var vm = await _service.BuildAsync(AsOf, includeProliferation: true, includeTraining: true);

        Assert.Equal(new[] { 2022, 2023, 2024, 2026 }, vm.ProliferationByYear.Select(point => point.Year));
        Assert.Equal(new[] { 100, 23, 0, 2 }, vm.ProliferationByYear.Select(point => point.Sdd));
        Assert.Equal(new[] { 0, 0, 40, 0 }, vm.ProliferationByYear.Select(point => point.Abw515));
        Assert.Equal(165, vm.ProliferationUnitsDelivered);
        Assert.Equal(3, vm.ProliferationReceivingUnits); // 56 Inf Bde, 12 Armd Regt, AMC Centre
        Assert.Equal(2, vm.ProliferationProjects);
        Assert.Equal(2, vm.ProliferationThisYear);
        Assert.Equal(2026, vm.ProliferationCurrentYear);

        var row2023 = vm.ProliferationBoard.Single(group => group.Key == "2023");
        Assert.Equal(23, row2023.Count);
        Assert.Equal("2 projects · 2 receiving units", row2023.Hint);
        Assert.Equal(new[] { "VR RL", "VR CMC" }, row2023.Projects.Select(project => project.ProjectName));
        Assert.Equal("20 units delivered · 2 receiving units", row2023.Projects[0].Detail);
        Assert.Equal("2026", vm.ProliferationBoard[0].Key); // latest year first
    }

    [Fact]
    public async Task Training_PlacesCoursesInTrainingYears_CountsEachCourseOnce_AndListsThemUnderEachProject()
    {
        var vrRl = AddProject("VR RL", _arVr);
        var vrCmc = AddProject("VR CMC", _arVr);
        var firing = AddProject("Firing range", _firing);
        AddTraining(new DateOnly(2024, 2, 10), 2, 3, 10, vrRl);            // Feb 2024 is training year 2023-24
        AddTraining(new DateOnly(2024, 5, 10), 1, 1, 8, vrRl, vrCmc);      // one course, two AR/VR projects: counted once
        AddTraining(new DateOnly(2026, 6, 1), 0, 2, 4, vrCmc);             // training year 2026-27: this year
        AddTraining(new DateOnly(2024, 6, 1), 5, 5, 5, firing);            // not AR/VR: ignored
        AddTraining(null, 9, 9, 9, vrRl);                                  // no date at all: cannot be placed in a year
        await _db.SaveChangesAsync();

        var vm = await _service.BuildAsync(AsOf, includeProliferation: true, includeTraining: true);

        Assert.Equal(new[] { "2023-24", "2024-25", "2026-27" }, vm.TrainingByYear.Select(point => point.Label));
        Assert.Equal(new[] { 15, 10, 6 }, vm.TrainingByYear.Select(point => point.People));
        Assert.Equal(new[] { 1, 1, 1 }, vm.TrainingByYear.Select(point => point.Courses));
        Assert.Equal(new[] { 1, 2, 1 }, vm.TrainingByYear.Select(point => point.Projects));
        Assert.Equal(3, vm.TrainingCourses);
        Assert.Equal(31, vm.TrainingPeople);
        Assert.Equal(2, vm.TrainingProjects);
        Assert.Equal(6, vm.TrainingPeopleThisYear);
        Assert.Equal("2026-27", vm.TrainingCurrentYearLabel);

        var row2024 = vm.TrainingBoard.Single(group => group.Key == "2024");
        Assert.Equal("2024-25", row2024.Name);
        Assert.Equal(10, row2024.Count);
        Assert.Equal("1 course · 2 projects", row2024.Hint);
        // The shared course is listed under both projects (the project figures add up to more than the row).
        Assert.Equal(2, row2024.Projects.Count);
        Assert.All(row2024.Projects, project => Assert.Equal("1 course · 10 people", project.Detail));
    }

    [Fact]
    public async Task NoArVrProjects_IsEmptyButLabelled()
    {
        AddProject("Firing range", _firing);
        await _db.SaveChangesAsync();

        var vm = await _service.BuildAsync(AsOf, includeProliferation: true, includeTraining: true);

        Assert.False(vm.HasData);
        Assert.Equal(2026, vm.ProliferationCurrentYear);
        Assert.Equal("2026-27", vm.TrainingCurrentYearLabel);
    }

    [Fact]
    public async Task ACallerWithoutTheTrainingPolicy_GetsNoTrainingFigures()
    {
        var vrRl = AddProject("VR RL", _arVr);
        AddYearly(vrRl, 2023, 10);
        AddTraining(new DateOnly(2024, 5, 10), 1, 1, 8, vrRl);
        await _db.SaveChangesAsync();

        var vm = await _service.BuildAsync(AsOf, includeProliferation: true, includeTraining: false);

        Assert.True(vm.CanViewProliferation);
        Assert.False(vm.CanViewTraining);
        Assert.Equal(10, vm.ProliferationUnitsDelivered);
        Assert.Equal(0, vm.TrainingPeople);
        Assert.Empty(vm.TrainingByYear);
        Assert.Empty(vm.TrainingBoard);

        var neither = await _service.BuildAsync(AsOf, includeProliferation: false, includeTraining: false);
        Assert.False(neither.ShowsAnything);
        Assert.False(neither.HasData);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
