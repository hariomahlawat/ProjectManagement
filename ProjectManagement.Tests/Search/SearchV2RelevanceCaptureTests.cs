using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

/// <summary>
/// Opt-in relevance and latency capture for Search V2 against a real PostgreSQL corpus.
/// It never runs in CI. Set:
///   PRISM_SEARCHV2_BENCH_CONNECTION  connection string to a development/test database
///   PRISM_SEARCHV2_BENCH_DATASET     dataset JSON (tools/search-v2-relevance-dataset.schema.json)
///   PRISM_SEARCHV2_BENCH_OUTPUT      output directory for results.json, timings.json and plans
/// Optional:
///   PRISM_SEARCHV2_BENCH_REBUILD=1   rebuild the active index generation for this build's ProjectionVersion first
///   PRISM_SEARCHV2_BENCH_EXPLAIN     ';'-separated queries whose plans are captured with auto_explain
/// Score results with: node tools/search-v2-relevance-evaluator.mjs dataset.json results.json
/// The dataset and outputs may contain corpus text; keep them outside the repository.
/// </summary>
public sealed class SearchV2RelevanceCaptureTests
{
    private const int TimedRuns = 7;

    [Fact]
    public async Task CaptureRelevanceRun()
    {
        var connectionString = Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_CONNECTION");
        var datasetPath = Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_DATASET");
        var outputDirectory = Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_OUTPUT");
        if (string.IsNullOrWhiteSpace(connectionString)
            || string.IsNullOrWhiteSpace(datasetPath)
            || string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        var options = Options.Create(new SearchV2Options());
        if (Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_REBUILD") == "1")
        {
            await using var rebuildDb = CreateDb(connectionString);
            var builder = new SearchProjectionBuilder(rebuildDb, new UrlBuilder(), new SearchQueryNormalizer(), options);
            var projections = await builder.BuildAllAsync(CancellationToken.None);
            await new SearchIndexStore(rebuildDb).ReplaceFullGenerationAsync(projections, options.Value.ProjectionVersion, CancellationToken.None);
        }

        using var dataset = JsonDocument.Parse(await File.ReadAllTextAsync(datasetPath));
        var queries = dataset.RootElement.GetProperty("queries").EnumerateArray()
            .Select(item => (Id: item.GetProperty("id").GetString()!, Query: item.GetProperty("query").GetString()!))
            .ToArray();

        var results = new List<object>();
        var timings = new List<object>();
        foreach (var (id, query) in queries)
        {
            var searchTimes = new List<double>();
            var suggestTimes = new List<double>();
            SearchResponse? response = null;
            IReadOnlyList<SearchSuggestion> suggestions = Array.Empty<SearchSuggestion>();
            for (var run = 0; run <= TimedRuns; run++)
            {
                await using var db = CreateDb(connectionString);
                var engine = CreateEngine(db, options);
                var stopwatch = Stopwatch.StartNew();
                response = await engine.SearchAsync(new SearchRequest(query, PageSize: 20), Principal(), CancellationToken.None);
                stopwatch.Stop();
                if (run > 0) searchTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

                stopwatch.Restart();
                suggestions = await engine.SuggestAsync(query, Principal(), 6, CancellationToken.None);
                stopwatch.Stop();
                if (run > 0) suggestTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            results.Add(new
            {
                id,
                query,
                status = response!.ExecutionStatus.ToString(),
                totalHits = response.TotalHits,
                correctedQuery = response.CorrectedQuery,
                results = response.Results.Select(item => new
                {
                    entityType = item.EntityType,
                    entityKey = item.EntityKey,
                    title = item.Title,
                    tier = item.MatchTier,
                    channels = item.MatchChannels
                }),
                suggestions = suggestions.Select(item => item.Title)
            });
            timings.Add(new { id, query, searchMedianMs = Median(searchTimes), suggestMedianMs = Median(suggestTimes) });
        }

        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "results.json"), JsonSerializer.Serialize(new { queries = results }, json));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "timings.json"), JsonSerializer.Serialize(timings, json));

        var explain = Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_EXPLAIN");
        foreach (var query in (explain ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await using var db = CreateDb(connectionString);
            var plans = await CapturePlansAsync(db, async () =>
            {
                var engine = CreateEngine(db, options);
                await engine.SearchAsync(new SearchRequest(query, PageSize: 20), Principal(), CancellationToken.None);
                await engine.SuggestAsync(query, Principal(), 6, CancellationToken.None);
            });
            var safeName = string.Concat(query.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"plan-{safeName}.txt"), string.Join("\n\n-----\n\n", plans));
        }
    }

    [Fact]
    public async Task KeysetPaging_WalksTheWholeRankedSetWithoutGapsOrRepeats()
    {
        var connectionString = Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_CONNECTION");
        var query = Environment.GetEnvironmentVariable("PRISM_SEARCHV2_BENCH_PAGING_QUERY") ?? "simulator";
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var options = Options.Create(new SearchV2Options());
        var seen = new List<(string EntityType, string EntityKey, int Rank)>();
        string? cursor = null;
        long filteredHits;
        do
        {
            await using var db = CreateDb(connectionString);
            var response = await CreateEngine(db, options).SearchAsync(
                new SearchRequest(query, Cursor: cursor, PageSize: 7),
                Principal(),
                CancellationToken.None);
            Assert.Equal(SearchV2ExecutionStatus.Success, response.ExecutionStatus);
            filteredHits = response.FilteredHits;
            seen.AddRange(response.Results.Select(result => (result.EntityType, result.EntityKey, result.Rank)));
            cursor = response.NextCursor;
        }
        while (cursor is not null && seen.Count < 2000);

        Assert.Equal(filteredHits, seen.Count);
        Assert.Equal(seen.Count, seen.Select(item => (item.EntityType, item.EntityKey)).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, seen.Count), seen.Select(item => item.Rank));

        await using var staleDb = CreateDb(connectionString);
        var stale = await CreateEngine(staleDb, options).SearchAsync(
            new SearchRequest(query, Cursor: "eyJRdWVyeUhhc2giOiJ4IiwiUmFuayI6MjAsIkFjdGl2ZUdlbmVyYXRpb24iOjF9", PageSize: 7),
            Principal(),
            CancellationToken.None);
        Assert.Equal(SearchV2ExecutionStatus.StaleCursor, stale.ExecutionStatus);
        Assert.True(stale.IsReady);
        Assert.Equal(1, stale.Results.FirstOrDefault()?.Rank ?? 1);
    }

    private static async Task<IReadOnlyList<string>> CapturePlansAsync(ApplicationDbContext db, Func<Task> action)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        var plans = new List<string>();
        connection.Notice += (_, args) =>
        {
            if (args.Notice.MessageText.Contains("Query Text:", StringComparison.Ordinal)) plans.Add(args.Notice.MessageText);
        };
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                LOAD 'auto_explain';
                SET auto_explain.log_min_duration = 0;
                SET auto_explain.log_analyze = on;
                SET auto_explain.log_buffers = on;
                SET auto_explain.log_timing = on;
                SET auto_explain.log_nested_statements = on;
                SET client_min_messages = log;
                """;
            await setup.ExecuteNonQueryAsync();
        }

        await action();
        return plans;
    }

    private static ApplicationDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private static SearchEngine CreateEngine(ApplicationDbContext db, IOptions<SearchV2Options> options)
    {
        var normalizer = new SearchQueryNormalizer();
        return new SearchEngine(
            db,
            new SearchIndexStore(db),
            new AllPoliciesAccessFactory(),
            normalizer,
            new SearchAliasProvider(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<SearchAliasProvider>.Instance),
            new SearchCorrectionService(options, normalizer, NullLogger<SearchCorrectionService>.Instance),
            new SearchHighlightService(options),
            new SearchCursorCodec(),
            options,
            NullLogger<SearchEngine>.Instance);
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "search-bench") }, "bench"));

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var ordered = values.OrderBy(value => value).ToArray();
        return Math.Round(ordered[ordered.Length / 2], 2);
    }

    private sealed class AllPoliciesAccessFactory : ISearchAccessContextFactory
    {
        public Task<SearchAccessContext> CreateAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
            Task.FromResult(new SearchAccessContext(
                "search-bench",
                Array.Empty<string>(),
                new[]
                {
                    Policies.Documents.View,
                    Policies.Ipr.View,
                    ProjectOfficeReportsPolicies.ViewVisits,
                    ProjectOfficeReportsPolicies.ViewTotTracker,
                    ProjectOfficeReportsPolicies.ViewTrainingTracker,
                    ProjectOfficeReportsPolicies.ViewProliferationTracker,
                    ProjectOfficeReportsPolicies.ViewArpp
                }));
    }
}
