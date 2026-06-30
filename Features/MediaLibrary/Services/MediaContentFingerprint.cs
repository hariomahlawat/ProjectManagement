using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Creates fixed-length, deterministic fingerprints suitable for bounded catalogue columns.
/// Only source-content identity belongs in these fingerprints; descriptive parent metadata
/// must be updated independently so that renaming or rescheduling a source never invalidates
/// derivatives, classification, faces, embeddings or human-reviewed identity assignments.
/// </summary>
public static class MediaContentFingerprint
{
    public static string ComputeSha256(params object?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var builder = new StringBuilder(parts.Length * 32);
        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('|');
            }

            AppendEscaped(builder, ConvertPart(parts[index]));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string ConvertPart(object? value)
        => value switch
        {
            null => string.Empty,
            byte[] bytes => Convert.ToHexString(bytes),
            DateTimeOffset offset => offset.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            if (character is '\\' or '|')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }
    }
}
