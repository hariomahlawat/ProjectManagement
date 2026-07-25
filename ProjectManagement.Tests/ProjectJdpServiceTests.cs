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
    public async Task LinkProject_AllowsASecondDifferentJdpForSameProject()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        await fixture.Service.LinkProjectAsync(20, 1, User("editor-1"));

        Assert.Equal(2, await fixture.Context.IndustryPartnerProjects.CountAsync(link => link.ProjectId == 1));
        Assert.True(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 1 && link.IndustryPartnerId == 10));
        Assert.True(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 1 && link.IndustryPartnerId == 20));
    }

    [Fact]
    public async Task LinkProject_RejectsDuplicateProjectOrganisationLink()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        var exception = await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
            fixture.Service.LinkProjectAsync(10, 1, User("editor-1")));

        Assert.Contains(
            exception.Errors.SelectMany(entry => entry.Value),
            message => message.Contains("already linked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MultiJdpProfile_ReturnsEachPartnerWithItsOwnOtherProjects()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.Service.AddProjectJdpAsync(1, 20, User("editor-1"));

        var profile = await fixture.Service.GetProjectMultiJdpProfileAsync(1);

        Assert.Equal(2, profile.Count);
        Assert.Equal("2 JDPs linked", profile.CardTitle);
        Assert.Equal(new[] { "Alpha Systems", "Bravo Dynamics" }, profile.Partners.Select(partner => partner.Name));
        Assert.Equal(2, profile.Partners[0].OtherProjectCount);
        Assert.Empty(profile.Partners[1].OtherProjects);
    }

    [Fact]
    public async Task RemoveProjectJdp_RemovesOnlyTheSelectedLink()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.Service.AddProjectJdpAsync(1, 20, User("editor-1"));

        var profile = await fixture.Service.RemoveProjectJdpAsync(1, 10, User("editor-1"));

        Assert.Single(profile.Partners);
        Assert.Equal(20, profile.Partners[0].Id);
        Assert.False(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 1 && link.IndustryPartnerId == 10));
        Assert.True(await fixture.Context.IndustryPartnerProjects.AnyAsync(link =>
            link.ProjectId == 2 && link.IndustryPartnerId == 10));
    }

    [Fact]
    public async Task SearchProjectJdpOptions_ExcludesOrganisationsAlreadyLinkedToTheProject()
    {
        await using var fixture = await JdpFixture.CreateAsync();
        await fixture.SeedAsync();

        var alpha = await fixture.Service.SearchProjectJdpOptionsAsync(1, "Alpha", 10);
        var bravo = await fixture.Service.SearchProjectJdpOptionsAsync(1, "Bravo", 10);

        Assert.Empty(alpha);
        var option = Assert.Single(bravo);
        Assert.False(option.IsLinkedToProject);
        Assert.Equal(0, option.OtherProjectCount);
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
