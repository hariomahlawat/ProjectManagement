using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectOfficeReports.Training;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class ManageModelTests
{
    [Fact]
    public async Task OnPostSaveAsync_AllowsNonLegacySubmissionWithZeroLegacyCounts()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var trainingType = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Induction Course",
            IsActive = true,
            DisplayOrder = 1,
            CreatedByUserId = "seed",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        db.TrainingTypes.Add(trainingType);
        await db.SaveChangesAsync();

        var readService = new TrainingTrackerReadService(db);
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var writeService = new TrainingWriteService(db, clock, new NoOpTrainingNotificationService(), NullLogger<TrainingWriteService>.Instance);
        var optionsSnapshot = new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions { Enabled = true });
        var userContext = new StubUserContext("creator");

        var page = new ManageModel(optionsSnapshot, readService, writeService, userContext)
        {
            Input = new ManageModel.InputModel
            {
                TrainingTypeId = trainingType.Id,
                ScheduleMode = ManageModel.TrainingScheduleMode.DateRange,
                StartDate = new DateOnly(2024, 1, 1),
                EndDate = new DateOnly(2024, 1, 5),
                ProjectIds = new List<int>(),
                LegacyOfficerCount = 0,
                LegacyJcoCount = 0,
                LegacyOrCount = 0,
                IsLegacyRecord = false,
                HasRoster = false
            }
        };

        ConfigurePageContext(page);

        var result = await page.OnPostSaveAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Manage", redirect.PageName);
        Assert.True(page.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostSaveAsync_RejectsLegacySubmissionWithoutLegacyCounts()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var trainingType = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Conversion Course",
            IsActive = true,
            DisplayOrder = 1,
            CreatedByUserId = "seed",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        db.TrainingTypes.Add(trainingType);
        await db.SaveChangesAsync();

        var readService = new TrainingTrackerReadService(db);
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var writeService = new TrainingWriteService(db, clock, new NoOpTrainingNotificationService(), NullLogger<TrainingWriteService>.Instance);
        var optionsSnapshot = new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions { Enabled = true });
        var userContext = new StubUserContext("creator");

        var page = new ManageModel(optionsSnapshot, readService, writeService, userContext)
        {
            Input = new ManageModel.InputModel
            {
                TrainingTypeId = trainingType.Id,
                ScheduleMode = ManageModel.TrainingScheduleMode.DateRange,
                StartDate = new DateOnly(2024, 2, 1),
                EndDate = new DateOnly(2024, 2, 3),
                ProjectIds = new List<int>(),
                LegacyOfficerCount = 0,
                LegacyJcoCount = 0,
                LegacyOrCount = 0,
                IsLegacyRecord = true,
                HasRoster = true
            }
        };

        ConfigurePageContext(page);

        var result = await page.OnPostSaveAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        var legacyErrors = page.ModelState[string.Empty]?.Errors;
        Assert.NotNull(legacyErrors);
        Assert.Contains(legacyErrors!, error => error.ErrorMessage.Contains("legacy counts", StringComparison.OrdinalIgnoreCase));
        Assert.False(page.Input.HasRoster);
    }

    [Fact]
    public async Task OnPostSaveAsync_PersistsRosterAndCountersForNewTraining()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var trainingType = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Signal Orientation",
            IsActive = true,
            DisplayOrder = 1,
            CreatedByUserId = "seed",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        db.TrainingTypes.Add(trainingType);
        await db.SaveChangesAsync();

        var readService = new TrainingTrackerReadService(db);
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var writeService = new TrainingWriteService(db, clock, new NoOpTrainingNotificationService(), NullLogger<TrainingWriteService>.Instance);
        var optionsSnapshot = new StubOptionsSnapshot<TrainingTrackerOptions>(new TrainingTrackerOptions { Enabled = true });
        var userContext = new StubUserContext("creator");

        var rosterPayload = EncodeRosterPayload(
            new { armyNumber = "A001", rank = "Capt", name = "Alpha", unitName = "Signals", category = 0 },
            new { armyNumber = "A002", rank = "Subedar", name = "Bravo", unitName = "Signals", category = 1 },
            new { armyNumber = "A003", rank = "Sep", name = "Charlie", unitName = "Signals", category = 2 });

        var page = new ManageModel(optionsSnapshot, readService, writeService, userContext)
        {
            Input = new ManageModel.InputModel
            {
                TrainingTypeId = trainingType.Id,
                ScheduleMode = ManageModel.TrainingScheduleMode.DateRange,
                StartDate = new DateOnly(2024, 3, 1),
                EndDate = new DateOnly(2024, 3, 5),
                ProjectIds = new List<int>(),
                LegacyOfficerCount = 0,
                LegacyJcoCount = 0,
                LegacyOrCount = 0,
                IsLegacyRecord = false,
                RosterPayload = rosterPayload
            }
        };

        ConfigurePageContext(page);

        var result = await page.OnPostSaveAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Manage", redirect.PageName);
        var trainingId = Assert.IsType<Guid>(redirect.RouteValues!["id"]);

        var training = await db.Trainings
            .Include(t => t.Counters)
            .Include(t => t.Trainees)
            .SingleAsync(t => t.Id == trainingId);

        Assert.Equal(3, training.Trainees.Count);
        Assert.Equal(TrainingCounterSource.Roster, training.Counters!.Source);
        Assert.Equal(1, training.Counters.Officers);
        Assert.Equal(1, training.Counters.JuniorCommissionedOfficers);
        Assert.Equal(1, training.Counters.OtherRanks);
        Assert.Equal(3, training.Counters.Total);

        var trackerItems = await readService.SearchAsync(null, CancellationToken.None);
        var item = Assert.Single(trackerItems);
        Assert.Equal(TrainingCounterSource.Roster, item.CounterSource);
        Assert.Equal(1, item.CounterOfficers);
        Assert.Equal(1, item.CounterJcos);
        Assert.Equal(1, item.CounterOrs);
        Assert.Equal(3, item.CounterTotal);
    }

    private static string EncodeRosterPayload(params object[] rows)
    {
        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static void ConfigurePageContext(PageModel page)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private sealed class StubOptionsSnapshot<T> : IOptionsSnapshot<T> where T : class, new()
    {
        public StubOptionsSnapshot(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public T Get(string? name) => Value;
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(string userId)
        {
            UserId = userId;
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test"));
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class NoOpTrainingNotificationService : ITrainingNotificationService
    {
        public Task NotifyDeleteApprovedAsync(TrainingDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task NotifyDeleteRejectedAsync(TrainingDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task NotifyDeleteRequestedAsync(TrainingDeleteNotificationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
