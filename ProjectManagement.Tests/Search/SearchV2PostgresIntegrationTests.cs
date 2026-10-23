using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

/// <summary>
/// Real PostgreSQL tests for Search V2. Set PRISM_SEARCHV2_TEST_CONNECTION to a server the
/// tests may create databases on; each run creates a disposable prism_test_searchv2_*
/// database, applies the full migration chain to it and drops it afterwards. They do not
/// use EF InMemory because it cannot validate PostgreSQL FTS, pg_trgm, triggers or locks.
/// Without the variable every test reports as skipped rather than passing vacuously.
/// </summary>
public sealed class SearchV2PostgresIntegrationTests : IClassFixture<SearchV2PostgresDatabase>
{
    private readonly SearchV2PostgresDatabase _database;

    public SearchV2PostgresIntegrationTests(SearchV2PostgresDatabase database)
    {
        _database = database;
    }

    private string ConnectionString => _database.ConnectionString;

    [SearchV2PostgresFact]
    public async Task PostgreSql_SearchPrimitives_AreAvailable()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm'),
                to_tsvector('english', 'filed filing') @@ websearch_to_tsquery('english', 'filing'),
                similarity('hyderbad', 'hyderabad') > similarity('hyderbad', 'secunderabad');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
    }

    [SearchV2PostgresFact]
    public async Task SearchV2_ActiveGeneration_IsQueryableWhenSchemaIsInstalled()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s."ActiveGeneration", s."IndexVersion", COUNT(e."Id")
            FROM "SearchIndexState" s
            LEFT JOIN "SearchEntries" e ON e."Generation" = s."ActiveGeneration"
            WHERE s."Id" = 1
            GROUP BY s."ActiveGeneration", s."IndexVersion";
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetInt64(0) >= 0);
        Assert.True(reader.GetInt32(1) >= 0);
        Assert.True(reader.GetInt64(2) >= 0);
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_TitlePhraseAndFinalTokenPrefix_PrimitivesMatchSearchV2Semantics()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                STRPOS(' mockup based pinaka high tech sml ', ' high tech ') > 0,
                to_tsvector('simple', 'Design and Devp of High Technology Transmitter')
                    @@ to_tsquery('simple', 'high & tech:*');
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_DisjunctiveFacetCounting_PreservesOtherFiltersAndCanonicalEntities()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH candidate_scored("CanonicalEntityType", "CanonicalEntityKey", "ResultCategory", "SourceModule", "Status") AS (
                VALUES
                    ('Project','1','Projects','Projects','Active'),
                    ('Project','1','Documents','Project documents','Published'),
                    ('Project','2','Projects','Projects','Completed'),
                    ('Project','3','Documents','Project documents','Published')
            ),
            category_scope AS (
                SELECT * FROM candidate_scored WHERE "SourceModule" = 'Project documents'
            ),
            source_scope AS (
                SELECT * FROM candidate_scored WHERE "ResultCategory" = 'Projects'
            )
            SELECT
                (SELECT COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey")) FROM category_scope),
                (SELECT COUNT(DISTINCT ("CanonicalEntityType", "CanonicalEntityKey")) FROM source_scope);
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_SummaryAndPagedRowsShareOneStatementCteScope()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH ranked("GlobalRank", "Title") AS (
                VALUES (1, 'A'), (2, 'B')
            ),
            summary AS (
                SELECT COUNT(*)::bigint AS "TotalHits" FROM ranked
            ),
            paged_results AS (
                SELECT * FROM ranked WHERE "GlobalRank" > 0 ORDER BY "GlobalRank" LIMIT 5
            )
            SELECT s."TotalHits", p."GlobalRank", p."Title"
            FROM summary s
            LEFT JOIN paged_results p ON TRUE
            ORDER BY p."GlobalRank" NULLS LAST;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal("A", reader.GetString(2));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal("B", reader.GetString(2));
        Assert.False(await reader.ReadAsync());
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_SummaryRowSurvivesAnEmptyPagedResult()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH ranked("GlobalRank") AS (
                VALUES (1), (2)
            ),
            summary AS (
                SELECT COUNT(*)::bigint AS "TotalHits" FROM ranked
            ),
            paged_results AS (
                SELECT * FROM ranked WHERE "GlobalRank" > 99 ORDER BY "GlobalRank" LIMIT 5
            )
            SELECT s."TotalHits", p."GlobalRank"
            FROM summary s
            LEFT JOIN paged_results p ON TRUE
            ORDER BY p."GlobalRank" NULLS LAST;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.True(reader.IsDBNull(1));
        Assert.False(await reader.ReadAsync());
    }

    [SearchV2PostgresFact]
    public async Task SearchV2_RuntimeAliasCatalogue_IsQueryableWhenSchemaIsInstalled()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM "SearchAliases"
            WHERE "IsActive"
              AND NULLIF(BTRIM("NormalizedAlias"), '') IS NOT NULL
              AND NULLIF(BTRIM("Expansion"), '') IS NOT NULL;
            """;

        var count = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.True(count >= 1);
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_ExactWholeTitleTokensOutrankFinalTokenPrefixSemantics()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                to_tsvector('simple', 'Mockup High Tech Simulator') @@ to_tsquery('simple', 'high & tech'),
                NOT (to_tsvector('simple', 'High Technology Transmitter') @@ to_tsquery('simple', 'high & tech')),
                to_tsvector('simple', 'High Technology Transmitter') @@ to_tsquery('simple', 'high & tech:*');
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_FuzzyFallbackGateSkipsTrigramWhenLexicalCandidatesExist()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH simple_fts("Id") AS (
                VALUES (101::bigint), (102::bigint)
            ), strong_candidate_count AS (
                SELECT COUNT(DISTINCT "Id")::int AS value
                FROM simple_fts
            )
            SELECT
                (SELECT value FROM strong_candidate_count) >= 1 AS skip_fuzzy,
                (SELECT value FROM strong_candidate_count) = 2 AS preserves_candidate_count;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_CorrectionShortlistCanRecoverDeletionHeavyHyderabadTypo()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                GREATEST(similarity('hyderabad', 'hydrbd'), word_similarity('hydrbd', 'hyderabad')) >= 0.18,
                GREATEST(similarity('hyderabad', 'hyderbad'), word_similarity('hyderbad', 'hyderabad'))
                    > GREATEST(similarity('hyderabad', 'hydrbd'), word_similarity('hydrbd', 'hyderabad'));
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }

    [SearchV2PostgresFact]
    public async Task PostgreSql_LiteralAndAliasFtsRemainIndependent()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                NOT (to_tsvector('simple', 'Management of Hi Tech Appointments') @@ websearch_to_tsquery('simple', 'high tech')),
                to_tsvector('simple', 'Management of Hi Tech Appointments') @@ websearch_to_tsquery('simple', '"hi tech"');
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }

    [SearchV2PostgresFact]
    public async Task Search_ResultsFacetsAndSuggestions_AreFilteredByPolicy()
    {
        await using var host = _database.CreateHost();
        await host.ActivateAsync(
            host.Entry("Project", "9001", "Projects", "Projects", "Kestrel radar programme", null),
            host.Entry("IprRecord", "9002", "IPR", "Trackers", "Kestrel radar patent", Policies.Ipr.View),
            host.Entry("Visit", "9003", "Visits", "Trackers", "Kestrel radar visit", ProjectOfficeReportsPolicies.ViewVisits));

        host.Access.Policies = Array.Empty<string>();
        var restricted = await host.SearchAsync("kestrel");
        var restrictedSuggestions = await host.SuggestAsync("kestrel");

        Assert.True(restricted.IsReady);
        Assert.Equal(new[] { "Projects" }, restricted.Results.Select(result => result.SourceModule).Distinct().ToArray());
        Assert.Equal(new[] { "Projects" }, restricted.Facets.Sources.Select(facet => facet.Value).ToArray());
        Assert.DoesNotContain(restricted.Facets.Categories, facet => facet.Value == "Trackers");
        Assert.All(restrictedSuggestions, suggestion => Assert.Equal("Projects", suggestion.SourceModule));
        Assert.NotEmpty(restrictedSuggestions);

        host.Access.Policies = new[] { Policies.Ipr.View };
        var withIpr = await host.SearchAsync("kestrel");
        var withIprSuggestions = await host.SuggestAsync("kestrel");

        Assert.Equal(
            new[] { "IPR", "Projects" },
            withIpr.Facets.Sources.Select(facet => facet.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(withIpr.Results, result => result.SourceModule == "Visits");
        Assert.Contains(withIprSuggestions, suggestion => suggestion.SourceModule == "IPR");
        Assert.DoesNotContain(withIprSuggestions, suggestion => suggestion.SourceModule == "Visits");
    }

    [SearchV2PostgresFact]
    public async Task SoftDeletedProject_LeavesTheIndex_AfterAWorkerPass()
    {
        await using var host = _database.CreateHost();
        var projectId = await host.SeedProjectAsync("Ospreyfalcon telemetry suite");
        await host.ResetIndexStateAsync();

        Assert.True(await host.Worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, await host.CountEntriesAsync("Project", projectId.ToString()));

        await host.ExecuteAsync($"""UPDATE "Projects" SET "IsDeleted" = TRUE WHERE "Id" = {projectId};""");
        Assert.True(await host.Worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(0, await host.CountEntriesAsync("Project", projectId.ToString()));
        Assert.Empty((await host.SearchAsync("ospreyfalcon")).Results);
    }

    [SearchV2PostgresFact]
    public async Task TrashedAndArchivedProjectDocuments_StayOutAfterAFullRebuild()
    {
        await using var host = _database.CreateHost();
        var liveProject = await host.SeedProjectAsync("Harrierlance live project");
        var trashedProject = await host.SeedProjectAsync("Harrierlance trashed project");
        var archivedProject = await host.SeedProjectAsync("Harrierlance archived project");
        var liveDocument = await host.SeedProjectDocumentAsync(liveProject, "Harrierlance live document");
        var trashedDocument = await host.SeedProjectDocumentAsync(trashedProject, "Harrierlance trashed document");
        var archivedDocument = await host.SeedProjectDocumentAsync(archivedProject, "Harrierlance archived document");
        await host.ExecuteAsync($"""UPDATE "Projects" SET "IsDeleted" = TRUE WHERE "Id" = {trashedProject};""");
        await host.ExecuteAsync($"""UPDATE "Projects" SET "IsArchived" = TRUE WHERE "Id" = {archivedProject};""");
        await host.ResetIndexStateAsync();

        Assert.True(await host.Worker.RunOnceAsync(CancellationToken.None));

        Assert.Equal(1, await host.CountEntriesAsync("ProjectDocument", liveDocument.ToString()));
        Assert.Equal(0, await host.CountEntriesAsync("ProjectDocument", trashedDocument.ToString()));
        Assert.Equal(0, await host.CountEntriesAsync("ProjectDocument", archivedDocument.ToString()));
    }

    [SearchV2PostgresFact]
    public async Task CorrectionFailure_StillServesTheValidV2Response()
    {
        await using var host = _database.CreateHost(services =>
            services.AddScoped<ISearchCorrectionService, ThrowingCorrectionService>());
        await host.ActivateAsync(host.Entry("Project", "9101", "Projects", "Projects", "Peregrine sonar programme", null));

        var response = await host.SearchAsync("zzqqxyzzy");

        Assert.True(response.IsReady);
        Assert.Equal(SearchV2ExecutionStatus.Success, response.ExecutionStatus);
        Assert.Null(response.CorrectedQuery);
    }

    [SearchV2PostgresFact]
    public async Task FullRebuild_RefusesToReplaceANewerProjectionVersion()
    {
        await using var host = _database.CreateHost();
        await host.ActivateAsync(host.Entry("Project", "9201", "Projects", "Projects", "Newer generation", null));
        var before = await host.ReadStateAsync();

        await host.WithStoreAsync(async store =>
        {
            await Assert.ThrowsAsync<SearchIndexSupersededException>(() =>
                store.ReplaceFullGenerationAsync(Array.Empty<SearchProjection>(), host.ProjectionVersion - 1, CancellationToken.None));
        });

        var after = await host.ReadStateAsync();
        Assert.Equal(before, after);
        Assert.Equal(1, await host.CountEntriesAsync("Project", "9201"));

        await host.WithStoreAsync(store =>
            store.ReplaceFullGenerationAsync(Array.Empty<SearchProjection>(), host.ProjectionVersion - 1, CancellationToken.None, allowDowngrade: true));
        Assert.Equal(host.ProjectionVersion - 1, (await host.ReadStateAsync()).IndexVersion);
    }

    [SearchV2PostgresFact]
    public async Task StaleLeaseRecovery_LeavesARunningFullRebuildRequestAlone()
    {
        await using var host = _database.CreateHost();
        await host.ExecuteAsync("""DELETE FROM "SearchIndexWorkItems";""");
        await host.WithStoreAsync(store => store.RequestFullRebuildAsync("tester", CancellationToken.None));
        await host.WithStoreAsync(store => store.EnqueueAsync("Project", "777", CancellationToken.None));
        await host.ExecuteAsync("""UPDATE "SearchIndexWorkItems" SET "Status" = 1, "StartedAtUtc" = NOW() - INTERVAL '2 hours';""");

        var recovered = 0;
        await host.WithStoreAsync(async store => recovered = await store.RecoverStaleWorkItemsAsync(TimeSpan.FromMinutes(10), CancellationToken.None));
        Assert.Equal(1, recovered);
        Assert.Equal(1, await host.ScalarAsync<long>("""SELECT COUNT(*) FROM "SearchIndexWorkItems" WHERE "Status" = 1 AND "EntityType" = '__FullRebuild__';"""));

        await host.WithStoreAsync(async store => recovered = await store.RecoverStaleWorkItemsAsync(TimeSpan.FromMinutes(10), CancellationToken.None, includeFullRebuildRequests: true));
        Assert.Equal(1, recovered);
        Assert.Equal(0, await host.ScalarAsync<long>("""SELECT COUNT(*) FROM "SearchIndexWorkItems" WHERE "Status" = 1;"""));
    }

    [SearchV2PostgresFact]
    public async Task RebuildLock_IsExclusiveAndBlocksIncrementalPasses()
    {
        await using var host = _database.CreateHost();
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ISearchIndexCoordinator>();
        var second = secondScope.ServiceProvider.GetRequiredService<ISearchIndexCoordinator>();

        await using (var rebuild = await first.TryAcquireFullRebuildAsync(CancellationToken.None))
        {
            Assert.NotNull(rebuild);
            Assert.Null(await second.TryAcquireFullRebuildAsync(CancellationToken.None));
            Assert.Null(await second.TryAcquireIncrementalAsync(CancellationToken.None));
        }

        await using (var incrementalA = await first.TryAcquireIncrementalAsync(CancellationToken.None))
        await using (var incrementalB = await second.TryAcquireIncrementalAsync(CancellationToken.None))
        {
            Assert.NotNull(incrementalA);
            Assert.NotNull(incrementalB);
            Assert.Null(await second.TryAcquireFullRebuildAsync(CancellationToken.None));
        }

        await using var afterRelease = await second.TryAcquireFullRebuildAsync(CancellationToken.None);
        Assert.NotNull(afterRelease);
    }

    private sealed class ThrowingCorrectionService : ISearchCorrectionService
    {
        public Task<string?> TryCorrectAsync(DbConnection connection, NormalizedSearchQuery query, SearchAccessContext access, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("correction vocabulary unavailable");
    }
}

/// <summary>Skips unless PRISM_SEARCHV2_TEST_CONNECTION names a PostgreSQL server.</summary>
internal sealed class SearchV2PostgresFactAttribute : FactAttribute
{
    public SearchV2PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SearchV2PostgresDatabase.ServerConnectionString))
        {
            Skip = "Set PRISM_SEARCHV2_TEST_CONNECTION to a PostgreSQL server to run the Search V2 integration tests.";
        }
    }
}

/// <summary>
/// One disposable, fully migrated database per test-class run.
/// </summary>
public sealed class SearchV2PostgresDatabase : IAsyncLifetime
{
    public static string? ServerConnectionString => Environment.GetEnvironmentVariable("PRISM_SEARCHV2_TEST_CONNECTION");

    private string? _databaseName;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerConnectionString)) return;

        _databaseName = $"prism_test_searchv2_{Guid.NewGuid():N}"[..40];
        await ExecuteOnServerAsync($"CREATE DATABASE \"{_databaseName}\";");
        ConnectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = _databaseName }.ConnectionString;

        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseName is null) return;
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);");
    }

    internal SearchV2TestHost CreateHost(Action<IServiceCollection>? configure = null) => new(ConnectionString, configure);

    private static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class SearchV2TestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public SearchV2TestHost(string connectionString, Action<IServiceCollection>? configure)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IUrlBuilder, UrlBuilder>();
        services.AddSearchV2(configuration);
        services.AddSingleton<ISearchAccessContextFactory>(Access);
        configure?.Invoke(services);
        _services = services.BuildServiceProvider();
        Options = _services.GetRequiredService<IOptions<SearchV2Options>>().Value;
        Worker = new SearchIndexWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptions<SearchV2Options>>(),
            NullLogger<SearchIndexWorker>.Instance);
    }

    public IServiceProvider Services => _services;
    public SearchV2Options Options { get; }
    public int ProjectionVersion => Options.ProjectionVersion;
    public SearchIndexWorker Worker { get; }
    public TestAccess Access { get; } = new();

    private static ClaimsPrincipal User { get; } =
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "search-tester") }, "Test"));

    public SearchProjection Entry(string entityType, string key, string sourceModule, string category, string title, string? policy) =>
        new(
            entityType,
            key,
            entityType,
            key,
            null,
            sourceModule,
            category,
            title,
            title.ToLowerInvariant(),
            null,
            $"/{entityType}/{key}",
            null,
            null,
            title,
            null,
            title.ToLowerInvariant(),
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            SearchVisibilityMode.Authenticated,
            policy,
            null,
            ProjectionVersion,
            new[] { new SearchProjectionTerm(title, title.ToLowerInvariant(), SearchTermKinds.Name) },
            Array.Empty<SearchProjectionPrincipal>(),
            "{}");

    public Task ActivateAsync(params SearchProjection[] entries) =>
        WithStoreAsync(store => store.ReplaceFullGenerationAsync(entries, ProjectionVersion, CancellationToken.None, allowDowngrade: true));

    public async Task WithStoreAsync(Func<ISearchIndexStore, Task> action)
    {
        using var scope = _services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ISearchIndexStore>());
    }

    public async Task<SearchIndexActiveState> ReadStateAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISearchIndexStore>().GetActiveStateAsync(CancellationToken.None);
    }

    public async Task<SearchResponse> SearchAsync(string query)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISearchV2Engine>()
            .SearchAsync(new SearchRequest(query), User, CancellationToken.None);
    }

    public async Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(string query)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISearchV2Engine>()
            .SuggestAsync(query, User, 10, CancellationToken.None);
    }

    /// <summary>Empties the index so the next worker tick performs a full rebuild.</summary>
    public async Task ResetIndexStateAsync()
    {
        await ExecuteAsync("""
            DELETE FROM "SearchIndexWorkItems";
            DELETE FROM "SearchEntries";
            UPDATE "SearchIndexState" SET "ActiveGeneration" = 0, "IndexVersion" = 0, "LastFullRebuildUtc" = NULL WHERE "Id" = 1;
            """);
    }

    public async Task<int> SeedProjectAsync(string name)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var creatorId = await EnsureUserAsync(db);
        var project = new Project
        {
            Name = name,
            CreatedByUserId = creatorId,
            CaseFileNumber = $"SV2-{Guid.NewGuid():N}"[..20],
            LifecycleStatus = ProjectLifecycleStatus.Active,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    public async Task<int> SeedProjectDocumentAsync(int projectId, string title)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var uploaderId = await EnsureUserAsync(db);
        var document = new ProjectDocument
        {
            ProjectId = projectId,
            Title = title,
            StorageKey = $"search-v2-tests/{Guid.NewGuid():N}.pdf",
            OriginalFileName = "evidence.pdf",
            ContentType = "application/pdf",
            Status = ProjectDocumentStatus.Published,
            UploadedByUserId = uploaderId,
            FileStamp = 1
        };
        db.ProjectDocuments.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    public async Task<long> CountEntriesAsync(string entityType, string entityKey)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM "SearchEntries" e
            JOIN "SearchIndexState" s ON s."Id" = 1 AND s."ActiveGeneration" = e."Generation"
            WHERE e."EntityType" = @type AND e."EntityKey" = @key;
            """;
        command.Parameters.Add(new NpgsqlParameter("type", entityType));
        command.Parameters.Add(new NpgsqlParameter("key", entityKey));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task ExecuteAsync(string sql)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static async Task<string> EnsureUserAsync(ApplicationDbContext db)
    {
        const string id = "search-v2-test-user";
        if (!await db.Users.AnyAsync(user => user.Id == id))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = "search.v2.tester",
                NormalizedUserName = "SEARCH.V2.TESTER",
                FullName = "Search V2 Tester",
                Rank = "Test"
            });
            await db.SaveChangesAsync();
        }

        return id;
    }

    internal sealed class TestAccess : ISearchAccessContextFactory
    {
        public IReadOnlyList<string> Policies { get; set; } = SearchSourcePolicies.AllPolicies;

        public Task<SearchAccessContext> CreateAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
            Task.FromResult(new SearchAccessContext("search-tester", Array.Empty<string>(), Policies));
    }
}
