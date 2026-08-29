using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectManagement.Services.SearchV2.Query;

public interface ISearchCursorCodec
{
    string Encode(string query, int rank);
    bool TryDecode(string query, string? cursor, out int rank);
}

public sealed class SearchCursorCodec : ISearchCursorCodec
{
    private sealed record Payload(string QueryHash, int Rank);

    public string Encode(string query, int rank)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(Hash(query), rank));
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public bool TryDecode(string query, string? cursor, out int rank)
    {
        rank = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(normalized));
            if (payload is null || payload.Rank < 0 || !string.Equals(payload.QueryHash, Hash(query), StringComparison.Ordinal))
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
