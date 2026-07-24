using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Services.IndustryPartners;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectJdpServiceTests
{
    [Fact]
    public async Task GetProjectJdpProfile_ReportsOtherOngoingAndCompletedProjects()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        var profile = await fixture.Service.GetProjectJdpProfileAsync(1);

        Assert.True(profile.HasJdp);
        Assert.Equal("Alpha Systems", profile.PartnerName);
        Assert.Equal(2, profile.OtherProjectCount);
        Assert.Equal(1, profile.OtherOngoingProjectCount);
        Assert.Equal(1, profile.OtherCompletedProjectCount);
        Assert.Equal("Also linked to 2 other projects · 1 ongoing · 1 completed", profile.CardSummary);
    }

    [Fact]
    public async Task SetProjectJdp_ReplacesOnlyThisProjectsLink()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        var profile = await fixture.Service.SetProjectJdpAsync(1, 20, User("editor-1"));

        Assert.Equal(20, profile.PartnerId);
        Assert.Equal("Bravo Dynamics", profile.PartnerName);
        Assert.True(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 1 && link.IndustryPartnerId == 20));
        Assert.False(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 1 && link.IndustryPartnerId == 10));
        Assert.True(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 2 && link.IndustryPartnerId == 10));
    }

    [Fact]
    public async Task LinkProject_RejectsSecondJdpForSameProject()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        var exception = await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
            fixture.Service.LinkProjectAsync(20, 1, User("editor-1")));

        Assert.Contains(
            exception.Errors.SelectMany(entry => entry.Value),
            message => message.Contains("already has JDP Alpha Systems", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SetProjectJdp_CorrectsLegacyMultipleLinksWithoutAffectingOtherProjects()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();
        fixture.Context.IndustryPartnerProjects.Add(Link(20, 1));
        await fixture.Context.SaveChangesAsync();

        var before = await fixture.Service.GetProjectJdpProfileAsync(1);
        Assert.True(before.HasMultipleProjectLinks);

        var profile = await fixture.Service.SetProjectJdpAsync(1, 20, User("editor-1"));

        Assert.False(profile.HasMultipleProjectLinks);
        Assert.Equal(20, profile.PartnerId);
        Assert.Single(await fixture.Context.IndustryPartnerProjects
            .Where(link => link.ProjectId == 1)
            .ToListAsync());
        Assert.True(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 2 && link.IndustryPartnerId == 10));
    }

    [Fact]
    public async Task SearchProjectJdpOptions_ExcludesTheContextProjectFromUsageCounts()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        var options = await fixture.Service.SearchProjectJdpOptionsAsync(1, "Alpha", 10);

        var option = Assert.Single(options);
        Assert.True(option.IsLinkedToProject);
        Assert.Equal(2, option.OtherProjectCount);
        Assert.Equal(1, option.OtherOngoingProjectCount);
        Assert.Equal(1, option.OtherCompletedProjectCount);
    }

    private static ClaimsPrincipal User(string userId) => new(
        new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId)
            },
            "Test"));

    private sealed class JdpFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private JdpFixture(
            SqliteConnection connection,
            ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
            Service = new IndustryPartnerService(context);
        }

        public ApplicationDbContext Context { get; }

        public IndustryPartnerService Service { get; }

        public static async Task<JdpFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new JdpFixture(connection, context);
        }

        public async Task SeedAsync()
        {
            Context.Projects.AddRange(
                Project(1, "Primary Project", ProjectLifecycleStatus.Active),
                Project(2, "Other Ongoing Project", ProjectLifecycleStatus.Active),
                Project(3, "Completed Project", ProjectLifecycleStatus.Completed));

            Context.IndustryPartners.AddRange(
                Partner(10, "Alpha Systems"),
                Partner(20, "Bravo Dynamics"));

            Context.IndustryPartnerProjects.AddRange(
                Link(10, 1),
                Link(10, 2),
                Link(10, 3));

            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Project Project(
            int id,
            string name,
            ProjectLifecycleStatus status) => new()
        {
            Id = id,
            Name = name,
            CreatedByUserId = "seed",
            CreatedAt = new DateTime(2026, 1, 1),
            LifecycleStatus = status,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        private static IndustryPartner Partner(int id, string name) => new()
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            CreatedByUserId = "seed",
            CreatedUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        private static IndustryPartnerProject Link(int partnerId, int projectId) => new()
        {
            IndustryPartnerId = partnerId,
            ProjectId = projectId,
            LinkedByUserId = "seed",
            LinkedUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
    }
}
