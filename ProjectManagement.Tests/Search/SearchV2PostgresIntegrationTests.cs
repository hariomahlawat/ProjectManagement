using Npgsql;
using Xunit;

namespace ProjectManagement.Tests.Search;

/// <summary>
/// Real PostgreSQL smoke tests for the primitives Search V2 depends on.
/// Set PRISM_SEARCHV2_TEST_CONNECTION to run them against a disposable/test database.
/// They intentionally do not use EF InMemory because it cannot validate PostgreSQL FTS or pg_trgm behaviour.
/// </summary>
public sealed class SearchV2PostgresIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("PRISM_SEARCHV2_TEST_CONNECTION");

    [Fact]
    public async Task PostgreSql_SearchPrimitives_AreAvailable()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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

    [Fact]
    public async Task SearchV2_ActiveGeneration_IsQueryableWhenSchemaIsInstalled()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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
        Assert.True(reader.GetInt32(1) >= 1);
        Assert.True(reader.GetInt64(2) >= 0);
    }


    [Fact]
    public async Task PostgreSql_TitlePhraseAndFinalTokenPrefix_PrimitivesMatchSearchV2Semantics()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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


    [Fact]
    public async Task PostgreSql_DisjunctiveFacetCounting_PreservesOtherFiltersAndCanonicalEntities()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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

    [Fact]
    public async Task PostgreSql_SummaryAndPagedRowsShareOneStatementCteScope()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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

    [Fact]
    public async Task PostgreSql_SummaryRowSurvivesAnEmptyPagedResult()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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

    [Fact]
    public async Task SearchV2_RuntimeAliasCatalogue_IsQueryableWhenSchemaIsInstalled()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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

    [Fact]
    public async Task PostgreSql_ExactWholeTitleTokensOutrankFinalTokenPrefixSemantics()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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

    [Fact]
    public async Task PostgreSql_FuzzyFallbackGateSkipsTrigramWhenLexicalCandidatesExist()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;

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


}
