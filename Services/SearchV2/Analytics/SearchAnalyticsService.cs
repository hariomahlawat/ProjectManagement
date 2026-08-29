using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2.Models;

namespace ProjectManagement.Services.SearchV2.Analytics;

public interface ISearchAnalyticsService
{
    Task LogQueryAsync(string query, ClaimsPrincipal user, long resultCount, long latencyMs, string engine, string? correction, CancellationToken cancellationToken);
    Task LogShadowAsync(string query, IReadOnlyList<GlobalSearchHit> legacy, IReadOnlyList<SearchResult> v2, CancellationToken cancellationToken);
    Task LogClickAsync(string query, string entityType, string entityKey, int rank, string sourceModule, CancellationToken cancellationToken);
    Task PruneAsync(int retentionDays, CancellationToken cancellationToken);
}

public sealed class SearchAnalyticsService : ISearchAnalyticsService
{
    private readonly ApplicationDbContext _db;

    public SearchAnalyticsService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task LogQueryAsync(
        string query,
        ClaimsPrincipal user,
        long resultCount,
        long latencyMs,
        string engine,
        string? correction,
        CancellationToken cancellationToken)
    {
        var roles = user.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .Take(12)
            .ToArray();
        var context = Limit(roles.Length == 0 ? "Authenticated" : string.Join(',', roles), 1000);
        query = Limit(query, 1000);
        engine = Limit(engine, 32);
        correction = string.IsNullOrWhiteSpace(correction) ? null : Limit(correction, 1000);

        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SearchQueryLogs" (
                "Query", "SearchedAtUtc", "UserContextCategory", "ResultCount", "LatencyMs", "Engine", "ZeroResult", "CorrectionOffered")
            VALUES ({query}, NOW(), {context}, {resultCount}, {latencyMs}, {engine}, {resultCount == 0}, {correction});
            """, cancellationToken);
    }

    public Task LogShadowAsync(
        string query,
        IReadOnlyList<GlobalSearchHit> legacy,
        IReadOnlyList<SearchResult> v2,
        CancellationToken cancellationToken)
    {
        query = Limit(query, 1000);
        var legacyTop = JsonSerializer.Serialize(legacy.Take(10).Select(hit => new { hit.Source, hit.Title, hit.Url }));
        var v2Top = JsonSerializer.Serialize(v2.Take(10).Select(hit => new { hit.SourceModule, hit.Title, hit.Url, hit.Rank }));
        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SearchShadowComparisons" ("Query", "ComparedAtUtc", "LegacyTopJson", "V2TopJson")
            VALUES ({query}, NOW(), CAST({legacyTop} AS jsonb), CAST({v2Top} AS jsonb));
            """, cancellationToken);
    }

    public Task LogClickAsync(
        string query,
        string entityType,
        string entityKey,
        int rank,
        string sourceModule,
        CancellationToken cancellationToken)
    {
        query = Limit(query, 1000);
        entityType = Limit(entityType, 64);
        entityKey = Limit(entityKey, 128);
        sourceModule = Limit(sourceModule, 96);
        rank = Math.Max(1, rank);
        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SearchClickLogs" ("Query", "ClickedAtUtc", "EntityType", "EntityKey", "SelectedRank", "SourceModule")
            VALUES ({query}, NOW(), {entityType}, {entityKey}, {rank}, {sourceModule});
            """, cancellationToken);
    }

    public Task PruneAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(retentionDays, 1, 3650);
        return _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "SearchQueryLogs" WHERE "SearchedAtUtc" < NOW() - ({days} * INTERVAL '1 day');
            DELETE FROM "SearchClickLogs" WHERE "ClickedAtUtc" < NOW() - ({days} * INTERVAL '1 day');
            DELETE FROM "SearchShadowComparisons" WHERE "ComparedAtUtc" < NOW() - ({days} * INTERVAL '1 day');
            """, cancellationToken);
    }

    private static string Limit(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

public sealed class SearchTelemetryRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Microsoft.Extensions.Options.IOptions<SearchV2Options> _options;
    private readonly ILogger<SearchTelemetryRetentionWorker> _logger;

    public SearchTelemetryRetentionWorker(
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<SearchV2Options> options,
        ILogger<SearchTelemetryRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var analytics = scope.ServiceProvider.GetRequiredService<ISearchAnalyticsService>();
                await analytics.PruneAsync(_options.Value.QueryLogRetentionDays, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Search telemetry retention pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
