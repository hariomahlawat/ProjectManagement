using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectManagement.Services.Startup;

internal static class IsoCountrySeedData
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<IsoCountrySeedRow> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "iso3166.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"ISO-3166 seed file was not found at '{path}'.", path);
        }

        using var stream = File.OpenRead(path);
        var rows = JsonSerializer.Deserialize<List<IsoCountrySeedRow>>(stream, Options);
        return rows ?? new List<IsoCountrySeedRow>();
    }

    internal sealed class IsoCountrySeedRow
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("alpha2")]
        public string? Alpha2 { get; init; }

        [JsonPropertyName("alpha3")]
        public string? Alpha3 { get; init; }

        [JsonPropertyName("numeric")]
        public string? Numeric { get; init; }
    }
}
