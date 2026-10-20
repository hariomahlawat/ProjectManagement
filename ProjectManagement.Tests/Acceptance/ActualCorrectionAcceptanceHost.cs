using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Tests.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests.Acceptance;

/// <summary>
/// The real PRISM host (routing, authorization, antiforgery, Razor rendering and DI) over
/// one shared SQLite database, so the Actual correction workflow is exercised through
/// HTTP exactly as a browser posts it, minus JavaScript. SQLite is relational, so the
/// proposal service's transactions and compare-and-set statements run for real; the
/// PostgreSQL lock behaviour is covered by the migration-gate fixtures instead.
/// The schema is created from the model. The startup migration boundary validates the
/// PostgreSQL catalogue, which SQLite cannot answer, so startup is given an empty
/// non-relational context (which it skips, as other endpoint tests do) and every request
/// after startup gets the shared SQLite database.
/// </summary>
public sealed class ActualCorrectionAcceptanceHost : IAsyncDisposable
{
    /// <summary>
    /// Each acceptance test boots a full in-process host; running them alongside other
    /// endpoint tests competes for the thread pool and can stall unrelated requests.
    /// </summary>
    public const string Collection = "Actual correction acceptance (isolated host)";

    public const string UserHeader = "X-Acceptance-User";
    public const string RolesHeader = "X-Acceptance-Roles";

    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    private ActualCorrectionAcceptanceHost(SqliteConnection connection, WebApplicationFactory<Program> factory)
    {
        _connection = connection;
        _factory = factory;
    }

    public static async Task<ActualCorrectionAcceptanceHost> CreateAsync(Func<ApplicationDbContext, Task> seed)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options))
        {
            await db.Database.EnsureCreatedAsync();
            await seed(db);
        }

        var started = new StartupGate();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UsePrismTestInfrastructure("actual-correction-acceptance");
            builder.ConfigureServices(services =>
            {
                // Every registration of the earlier InMemory context, including provider configuration.
                PrismTestWebHostBuilderExtensions.RemoveDbContextRegistrations<ApplicationDbContext>(services);
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    if (started.IsOpen)
                    {
                        options.UseSqlite(connection);
                    }
                    else
                    {
                        options.UseInMemoryDatabase("actual-correction-acceptance-startup-" + Guid.NewGuid().ToString("N"));
                    }
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Acceptance";
                        options.DefaultChallengeScheme = "Acceptance";
                        options.DefaultScheme = "Acceptance";
                    })
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Acceptance", _ => { });
            });
        });

        // Start the host (and its startup migration boundary) before switching to SQLite.
        _ = factory.Server;
        started.IsOpen = true;
        return new ActualCorrectionAcceptanceHost(connection, factory);
    }

    /// <summary>A browser-like client for one user: cookies kept, redirects not followed.</summary>
    public HttpClient ClientFor(string userId, params string[] roles)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Add(UserHeader, userId);
        client.DefaultRequestHeaders.Add(RolesHeader, string.Join(",", roles));
        return client;
    }

    /// <summary>Changes the user's roles for later requests, keeping the same browser session.</summary>
    public static void SetRoles(HttpClient client, params string[] roles)
    {
        client.DefaultRequestHeaders.Remove(RolesHeader);
        client.DefaultRequestHeaders.Add(RolesHeader, string.Join(",", roles));
    }

    public ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class StartupGate
    {
        public volatile bool IsOpen;
    }

    private sealed class HeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers[UserHeader].ToString();
            if (string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Name, userId) };
            claims.AddRange(Request.Headers[RolesHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Acceptance"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Acceptance")));
        }
    }
}

[CollectionDefinition(ActualCorrectionAcceptanceHost.Collection, DisableParallelization = true)]
public sealed class ActualCorrectionAcceptanceCollection
{
}
