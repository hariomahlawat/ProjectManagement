using System.Text;

namespace ProjectManagement.Infrastructure;

public static class SafeCsv
{
    private static readonly char[] FormulaPrefixCharacters = { '=', '+', '-', '@', '\t', '\r' };

    public static void AppendRow(StringBuilder builder, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AppendLine(string.Join(',', values.Select(value => Escape(value?.ToString()))));
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var neutralized = NeutralizeFormula(value);
        return $"\"{neutralized.Replace("\"", "\"\"")}\"";
    }

    public static byte[] ToUtf8WithBom(string content)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var payload = Encoding.UTF8.GetBytes(content ?? string.Empty);
        var result = new byte[preamble.Length + payload.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(payload, 0, result, preamble.Length, payload.Length);
        return result;
    }

    private static string NeutralizeFormula(string value)
    {
        var firstMeaningfulIndex = 0;
        while (firstMeaningfulIndex < value.Length && value[firstMeaningfulIndex] == ' ')
        {
            firstMeaningfulIndex++;
        }

        if (firstMeaningfulIndex >= value.Length)
        {
            return value;
        }

        var first = value[firstMeaningfulIndex];
        if (!FormulaPrefixCharacters.Contains(first))
        {
            return value;
        }

        return value.Insert(firstMeaningfulIndex, "'");
    }
}
