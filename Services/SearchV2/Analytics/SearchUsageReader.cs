using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.SearchV2.Analytics;

/// <summary>How search was used in a recent window, for the administrator's Search index page.</summary>
/// <param name="Searches">Searches answered (by Search V2, the basic engine, or the basic engine as a fallback).</param>
/// <param name="FallbackSearches">Searches Search V2 should have answered but the basic engine answered instead.</param>
/// <param name="TypicalMilliseconds">Median time to answer a search, end to end.</param>
/// <param name="NoResultShare">Share of searches (0–1) that found nothing.</param>
/// <param name="SuggestionTypicalMilliseconds">Median time for search-box suggestions, from this process only.</param>
public sealed record SearchUsageSummary(
    long Searches,
    long FallbackSearches,
    double? TypicalMilliseconds,
    double? NoResultShare,
    double? SuggestionTypicalMilliseconds)
{
    public static SearchUsageSummary Empty { get; } = new(0, 0, null, null, null);
}

public interface ISearchUsageReader
{
    Task<SearchUsageSummary> ReadAsync(TimeSpan window, CancellationToken cancellationToken);
}

public sealed class SearchUsageReader : ISearchUsageReader
{
    private readonly ApplicationDbContext _db;
    private readonly ISearchSuggestionLatencyRecorder _suggestionLatency;

    public SearchUsageReader(ApplicationDbContext db, ISearchSuggestionLatencyRecorder suggestionLatency)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _suggestionLatency = suggestionLatency ?? throw new ArgumentNullException(nameof(suggestionLatency));
    }

    public async Task<SearchUsageSummary> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var suggestions = _suggestionLatency.Snapshot(window);
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            // One row per committed search: "V2", "Legacy" or "Legacy-Fallback". Older builds
            // also wrote "V2-Suggest" and "V2-Engine" rows, which are not searches.
            command.CommandText = """
                SELECT COUNT(*)::bigint,
                       COUNT(*) FILTER (WHERE "Engine" = 'Legacy-Fallback')::bigint,
                       (percentile_cont(0.50) WITHIN GROUP (ORDER BY "LatencyMs"))::double precision,
                       AVG(CASE WHEN "ZeroResult" THEN 1.0 ELSE 0.0 END)::double precision
                FROM "SearchQueryLogs"
                WHERE "Engine" IN ('V2', 'Legacy', 'Legacy-Fallback')
                  AND "SearchedAtUtc" >= @since;
                """;
            var since = command.CreateParameter();
            since.ParameterName = "@since";
            since.Value = DateTime.UtcNow - window;
            command.Parameters.Add(since);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return SearchUsageSummary.Empty with { SuggestionTypicalMilliseconds = suggestions.P50Ms };
            }

            return new SearchUsageSummary(
                reader.GetInt64(0),
                reader.GetInt64(1),
                ReadDouble(reader, 2),
                ReadDouble(reader, 3),
                suggestions.P50Ms);
        }
        catch (DbException)
        {
            return SearchUsageSummary.Empty with { SuggestionTypicalMilliseconds = suggestions.P50Ms };
        }
        finally
        {
            try { await _db.Database.CloseConnectionAsync(); } catch { }
        }
    }

    private static double? ReadDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}
