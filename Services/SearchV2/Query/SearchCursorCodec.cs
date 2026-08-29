using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectManagement.Services.SearchV2.Query;

public interface ISearchCursorCodec
{
    string Encode(string query, int rank);
    bool TryDecode(string query, string? cursor, out int rank);
    string Encode(string query, int rank, long activeGeneration);
    bool TryDecode(string query, string? cursor, long activeGeneration, out int rank);
}

public sealed class SearchCursorCodec : ISearchCursorCodec
{
    private sealed record Payload(string QueryHash, int Rank, long ActiveGeneration);

    public string Encode(string query, int rank) => Encode(query, rank, 0);

    public bool TryDecode(string query, string? cursor, out int rank) =>
        TryDecode(query, cursor, 0, out rank);

    public string Encode(string query, int rank, long activeGeneration)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(Hash(query), rank, Math.Max(0, activeGeneration)));
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public bool TryDecode(string query, string? cursor, long activeGeneration, out int rank)
    {
        rank = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(normalized));
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
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Hash(string query)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(query.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 12));
    }
}
