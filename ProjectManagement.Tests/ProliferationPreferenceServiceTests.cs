using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationPreferenceServiceTests
{
    [Fact]
    public async Task SetPreferenceAsync_CreatesPreferenceAndLogsAudit()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 12, 5, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProliferationPreferenceService(context, clock, audit);

        var result = await service.SetPreferenceAsync(
            projectId: 42,
            source: ProliferationSource.Internal,
            year: 2024,
            userId: "user-1",
            expectedRowVersion: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(ProliferationPreferenceChangeOutcome.Created, result.Outcome);
        var preference = Assert.NotNull(result.Preference);
        Assert.Equal(42, preference.ProjectId);
        Assert.Equal(ProliferationSource.Internal, preference.Source);
        Assert.Equal(2024, preference.Year);
        Assert.Equal("user-1", preference.UserId);
        Assert.Equal(clock.UtcNow, preference.CreatedAtUtc);
        Assert.Equal(clock.UtcNow, preference.LastModifiedAtUtc);
        Assert.NotNull(preference.RowVersion);
        Assert.NotEmpty(preference.RowVersion);

        var saved = await context.ProliferationYearPreferences.SingleAsync();
        Assert.Equal(preference.Id, saved.Id);
        Assert.Equal(2024, saved.Year);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.Proliferation.PreferenceChanged", entry.Action);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal("Created", entry.Data["ChangeType"]);
        Assert.Equal("42", entry.Data["ProjectId"]);
        Assert.Equal("Internal", entry.Data["Source"]);
        Assert.Equal("2024", entry.Data["Year"]);
        Assert.Equal("user-1", entry.Data["PreferenceUserId"]);
    }

    [Fact]
    public async Task SetPreferenceAsync_WhenRowVersionProvidedForCreate_ReturnsConcurrencyConflict()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var audit = new RecordingAudit();
        var service = new ProliferationPreferenceService(context, clock, audit);

        var result = await service.SetPreferenceAsync(
            projectId: 77,
            source: ProliferationSource.External,
            year: 2025,
            userId: "user-2",
            expectedRowVersion: Guid.NewGuid().ToByteArray(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(ProliferationPreferenceChangeOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Null(result.Preference);
        Assert.Empty(audit.Entries);
        Assert.Empty(await context.ProliferationYearPreferences.ToListAsync());
    }

    [Fact]
    public async Task SetPreferenceAsync_WhenRowVersionDoesNotMatchExisting_ReturnsConcurrencyConflict()
    {
        await using var context = CreateContext();

        var existing = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 100,
            Source = ProliferationSource.External,
            Year = 2023,
            UserId = "user-3",
            CreatedByUserId = "user-3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        context.ProliferationYearPreferences.Add(existing);
        await context.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var audit = new RecordingAudit();
        var service = new ProliferationPreferenceService(context, clock, audit);

        var result = await service.SetPreferenceAsync(
            projectId: existing.ProjectId,
            source: existing.Source,
            year: 2024,
            userId: existing.UserId,
            expectedRowVersion: Guid.NewGuid().ToByteArray(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(ProliferationPreferenceChangeOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Null(result.Preference);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task SetPreferenceAsync_UpdatesExistingPreference()
    {
        await using var context = CreateContext();

        var existing = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 200,
            Source = ProliferationSource.Internal,
            Year = 2022,
            UserId = "user-4",
            CreatedByUserId = "user-4",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedByUserId = "user-4",
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        context.ProliferationYearPreferences.Add(existing);
        await context.SaveChangesAsync();

        var originalRowVersion = existing.RowVersion.ToArray();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProliferationPreferenceService(context, clock, audit);

        var result = await service.SetPreferenceAsync(
            existing.ProjectId,
            existing.Source,
            year: 2025,
            userId: existing.UserId,
            expectedRowVersion: originalRowVersion,
            cancellationToken: CancellationToken.None);

        Assert.Equal(ProliferationPreferenceChangeOutcome.Updated, result.Outcome);
        var updated = Assert.NotNull(result.Preference);
        Assert.Equal(2025, updated.Year);
        Assert.Equal(clock.UtcNow, updated.LastModifiedAtUtc);
        Assert.NotEmpty(updated.RowVersion);
        Assert.False(updated.RowVersion.AsSpan().SequenceEqual(originalRowVersion));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.Proliferation.PreferenceChanged", entry.Action);
        Assert.Equal("Updated", entry.Data["ChangeType"]);
        Assert.Equal("200", entry.Data["ProjectId"]);
        Assert.Equal("Internal", entry.Data["Source"]);
        Assert.Equal("2025", entry.Data["Year"]);
    }

    [Fact]
    public async Task ClearPreferenceAsync_RemovesPreferenceAndLogs()
    {
        await using var context = CreateContext();

        var preference = new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 300,
            Source = ProliferationSource.External,
            Year = 2024,
            UserId = "user-5",
            CreatedByUserId = "user-5",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedByUserId = "user-5",
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        context.ProliferationYearPreferences.Add(preference);
        await context.SaveChangesAsync();

        var rowVersion = preference.RowVersion.ToArray();
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var audit = new RecordingAudit();
        var service = new ProliferationPreferenceService(context, clock, audit);

        var result = await service.ClearPreferenceAsync(
            preference.ProjectId,
            preference.Source,
            preference.UserId,
            rowVersion,
            CancellationToken.None);

        Assert.Equal(ProliferationPreferenceChangeOutcome.Cleared, result.Outcome);
        Assert.Null(result.Preference);
        Assert.Empty(await context.ProliferationYearPreferences.ToListAsync());

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.Proliferation.PreferenceChanged", entry.Action);
        Assert.Equal("user-5", entry.UserId);
        Assert.Equal("Cleared", entry.Data["ChangeType"]);
        Assert.Equal("300", entry.Data["ProjectId"]);
        Assert.Equal("External", entry.Data["Source"]);
        Assert.Equal("2024", entry.Data["Year"]);
    }

    [Fact]
    public async Task ClearPreferenceAsync_WhenMissingPreference_ReturnsNoChangeForAutoFallback()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var audit = new RecordingAudit();
        var service = new ProliferationPreferenceService(context, clock, audit);

        var result = await service.ClearPreferenceAsync(
            projectId: 999,
            source: ProliferationSource.Internal,
            userId: "user-6",
            expectedRowVersion: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(ProliferationPreferenceChangeOutcome.NoChange, result.Outcome);
        Assert.Null(result.Preference);
        Assert.Empty(audit.Entries);
        Assert.Empty(await context.ProliferationYearPreferences.ToListAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
