using System.Text.Encodings.Web;
using System.Text.Json;

namespace ProjectManagement.Services.Admin;

public sealed record AdminAuditPayload(
    string? EntityType,
    string? EntityId,
    string? ActorUserId,
    string? ActorName,
    string? ActorRoles,
    string? Reason,
    string? Outcome,
    string? TraceId,
    string? Origin,
    string? BeforeJson,
    string? AfterJson,
    string? RawPrettyJson,
    IReadOnlyDictionary<string, string?>? Fields = null)
{
    /// <summary>
    /// A top-level value of the stored details ("ProjectId", "DeckName"), matched without regard
    /// to case. Null when the details do not name it.
    /// </summary>
    public string? Field(string name) =>
        Fields is not null && Fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    public string? AffectedRecord => string.IsNullOrWhiteSpace(EntityType)
        ? null
        : string.IsNullOrWhiteSpace(EntityId)
            ? EntityType
            : $"{EntityType} · {EntityId}";
}

public interface IAdminAuditPayloadParser
{
    AdminAuditPayload Parse(string? dataJson);
}

public sealed class AdminAuditPayloadParser : IAdminAuditPayloadParser
{
    public AdminAuditPayload Parse(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new(null, null, null, null, null, null, null, null, null, null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new(null, null, null, null, null, null, null, null, null, null, null, Pretty(dataJson));
            }

            return new AdminAuditPayload(
                ReadString(root, "EntityType"),
                ReadString(root, "EntityId"),
                ReadString(root, "ActorUserId"),
                ReadString(root, "ActorName"),
                ReadString(root, "ActorRoles"),
                ReadString(root, "Reason"),
                ReadString(root, "Outcome"),
                ReadString(root, "TraceId"),
                ReadString(root, "Origin"),
                PrettyNested(ReadString(root, "Before")),
                PrettyNested(ReadString(root, "After")),
                JsonSerializer.Serialize(root, PrettyJsonOptions),
                ReadFields(root));
        }
        catch (JsonException)
        {
            return new(null, null, null, null, null, null, null, null, null, null, null, dataJson.Trim());
        }
    }

    // Shows "R&D" and Hindi as written, older \uXXXX rows included. The result is text only:
    // Razor encodes it, and the audit drawer embeds it through an HTML-safe serialisation and
    // sets it with textContent.
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string? ReadString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => property.Value.GetRawText()
            };
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string?> ReadFields(JsonElement root)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            fields.TryAdd(property.Name, property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.Object or JsonValueKind.Array => null,
                _ => property.Value.GetRawText()
            });
        }

        return fields;
    }

    private static string? PrettyNested(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Pretty(value);
    }

    private static string Pretty(string value)
    {
        try
        {
            using var nested = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(nested.RootElement, PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return value.Trim();
        }
    }
}
