using System.Text;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class SafeCsvWriterTests
{
    [Fact]
    public void Write_QuotesStructuredValuesAndNeutralisesSpreadsheetFormulae()
    {
        var writer = new SafeCsvWriter();

        var bytes = writer.Write(
            new[] { "Name", "Value" },
            new[]
            {
                (IReadOnlyList<object?>)new object?[] { "A, B", "=HYPERLINK(\"https://example.invalid\")" }
            });

        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("\uFEFF", text);
        Assert.Contains("\"A, B\"", text);
        Assert.Contains("'=HYPERLINK", text);
    }
}
