using System.Text;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Tests;

public sealed class SafeCsvTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData(" +SUM(A1:A2)", "\" '+SUM(A1:A2)\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    [InlineData("normal", "\"normal\"")]
    public void EscapeNeutralisesSpreadsheetFormulaPrefixes(string input, string expected)
    {
        Assert.Equal(expected, SafeCsv.Escape(input));
    }

    [Fact]
    public void AppendRowEscapesQuotesCommasAndLineBreaks()
    {
        var builder = new StringBuilder();

        SafeCsv.AppendRow(builder, "a,b", "a\"b", "line1\nline2");

        Assert.Equal("\"a,b\",\"a\"\"b\",\"line1\nline2\"" + Environment.NewLine, builder.ToString());
    }

    [Fact]
    public void Utf8ExportIncludesBom()
    {
        var bytes = SafeCsv.ToUtf8WithBom("header");
        var preamble = Encoding.UTF8.GetPreamble();
        Assert.True(bytes.Take(preamble.Length).SequenceEqual(preamble));
    }
}
