using System.Text;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Admin;

public interface ISafeCsvWriter
{
    byte[] Write(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows);
}

public sealed class SafeCsvWriter : ISafeCsvWriter
{
    public byte[] Write(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        SafeCsv.AppendRow(builder, headers.Cast<object?>().ToArray());
        foreach (var row in rows)
        {
            SafeCsv.AppendRow(builder, row.ToArray());
        }

        return SafeCsv.ToUtf8WithBom(builder.ToString());
    }
}
