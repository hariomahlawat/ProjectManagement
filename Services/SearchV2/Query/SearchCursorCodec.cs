using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectManagement.Services.SearchV2.Query;

/// <summary>
/// Position of the last row on a page in the committed-search ordering
/// (tier, entity priority, fusion score DESC, event date DESC NULLS LAST, updated DESC, id).
/// </summary>
public sealed record SearchCursorPosition(
    int Tier,
    int EntityPriority,
    double Score,
    DateTimeOffset? EventDate,
    DateTimeOffset UpdatedAt,
    long Id,
    int Rank);

public enum SearchCursorState
{
    /// <summary>No cursor was supplied: first page.</summary>
    None = 0,
    /// <summary>The cursor belongs to this query and index generation.</summary>
    Valid = 1,
    /// <summary>The cursor is malformed, for another query, or for a replaced index generation.</summary>
    Stale = 2
}

public interface ISearchCursorCodec
{
    string Encode(string query, int rank);
    bool TryDecode(string query, string? cursor, out int rank);
    string Encode(string query, int rank, long activeGeneration);
    bool TryDecode(string query, string? cursor, long activeGeneration, out int rank);
    string EncodeKeyset(string query, SearchCursorPosition position, long activeGeneration);
    SearchCursorState DecodeKeyset(string query, string? cursor, long activeGeneration, out SearchCursorPosition? position);
}

public sealed class SearchCursorCodec : ISearchCursorCodec
{
    private sealed record KeysetPayload(int T, int P, double S, long? E, long U, long I);

    private sealed record Payload(string QueryHash, int Rank, long ActiveGeneration, KeysetPayload? K = null);

    public string Encode(string query, int rank) => Encode(query, rank, 0);

    public bool TryDecode(string query, string? cursor, out int rank) =>
        TryDecode(query, cursor, 0, out rank);

    public string Encode(string query, int rank, long activeGeneration) =>
        Serialize(new Payload(Hash(query), rank, Math.Max(0, activeGeneration)));

    public bool TryDecode(string query, string? cursor, long activeGeneration, out int rank)
    {
        rank = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;

        var payload = Deserialize(cursor);
        if (payload is null
            || payload.Rank < 0
            || payload.ActiveGeneration != Math.Max(0, activeGeneration)
            || !string.Equals(payload.QueryHash, Hash(query), StringComparison.Ordinal))
        {
            return false;
        }

        rank = payload.Rank;
        return true;
    }

    public string EncodeKeyset(string query, SearchCursorPosition position, long activeGeneration)
    {
        ArgumentNullException.ThrowIfNull(position);
        return Serialize(new Payload(
            Hash(query),
            position.Rank,
            Math.Max(0, activeGeneration),
            new KeysetPayload(
                position.Tier,
                position.EntityPriority,
                position.Score,
                position.EventDate?.UtcTicks,
                position.UpdatedAt.UtcTicks,
                position.Id)));
    }

    public SearchCursorState DecodeKeyset(string query, string? cursor, long activeGeneration, out SearchCursorPosition? position)
    {
        position = null;
        if (string.IsNullOrWhiteSpace(cursor)) return SearchCursorState.None;

        var payload = Deserialize(cursor);
        if (payload?.K is not { } key
            || payload.Rank < 0
            || payload.ActiveGeneration != Math.Max(0, activeGeneration)
            || !string.Equals(payload.QueryHash, Hash(query), StringComparison.Ordinal)
            || !double.IsFinite(key.S)
            || key.U < DateTimeOffset.MinValue.UtcTicks || key.U > DateTimeOffset.MaxValue.UtcTicks
            || key.E is < 0 or > 3155378975999999999)
        {
            return SearchCursorState.Stale;
        }

        position = new SearchCursorPosition(
            key.T,
            key.P,
            key.S,
            key.E.HasValue ? new DateTimeOffset(key.E.Value, TimeSpan.Zero) : null,
            new DateTimeOffset(key.U, TimeSpan.Zero),
            key.I,
            payload.Rank);
        return SearchCursorState.Valid;
    }

    private static string Serialize(Payload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static Payload? Deserialize(string cursor)
    {
        if (cursor.Length > 1024) return null;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(normalized));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(string query)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(query.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 12));
    }
}
